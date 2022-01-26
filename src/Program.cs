using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DatadogAssemblyPatcher
{
    class Program
    {
        private static NativeCallTargetDefinition[] _integrations;

        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: {entryPoint} {outputFolder} {Tracer home folder}");
                return;
            }

            var entryAssembly = args[0];
            var inputFolder = Path.GetDirectoryName(entryAssembly);
            var outputFolder = args[1];
            var tracerHomeFolder = args[2];

            var tracerPath = Path.Combine(tracerHomeFolder, "netcoreapp3.1", "Datadog.Trace.dll");

            _integrations = ReadIntegrations(tracerPath);

            var resolver = new DefaultAssemblyResolver();
            resolver.AddSearchDirectory(inputFolder);
            resolver.AddSearchDirectory(Path.Combine(tracerHomeFolder, "netcoreapp3.1"));

            var tracerAssembly = AssemblyDefinition.ReadAssembly(tracerPath);

            var consoleAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(inputFolder, "System.Console.dll"));

            var writeLineMethod = consoleAssembly.MainModule.Types
                .First(t => t.FullName == "System.Console")
                .Methods.First(m => m.Name == "WriteLine" && m.Parameters.Count == 1 && m.Parameters[0].ParameterType.Name == "String");

            var callTargetInvokerType = tracerAssembly.MainModule.Types.First(t => t.FullName == "Datadog.Trace.ClrProfiler.CallTarget.CallTargetInvoker");

            var callTargetStateType = tracerAssembly.MainModule.Types.First(t => t.FullName == "Datadog.Trace.ClrProfiler.CallTarget.CallTargetState");

            var callTargetReturnType = tracerAssembly.MainModule.Types.First(t => t.FullName == "Datadog.Trace.ClrProfiler.CallTarget.CallTargetReturn`1");
            //var callTargetReturnMethod = callTargetReturnType.Methods.First(t => t.Name == "GetReturnValue");

            var depsJson = Path.GetFileNameWithoutExtension(entryAssembly) + ".deps.json";

            foreach (var file in Directory.GetFiles(inputFolder, "*"))
            {
                File.Copy(file, Path.Combine(outputFolder, Path.GetFileName(file)), overwrite: true);

                AssemblyDefinition assembly;

                try
                {
                    assembly = AssemblyDefinition.ReadAssembly(file, new ReaderParameters { AssemblyResolver = resolver });
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (var module in assembly.Modules)
                {
                    module.Attributes |= ModuleAttributes.ILOnly;
                }

                bool isAssemblyModified = false;

                if (file == entryAssembly)
                {
                    isAssemblyModified = InjectStartupHook(assembly, tracerAssembly);
                }

                var writeLineMethodReference = assembly.MainModule.ImportReference(writeLineMethod);

                var importedMethods = new Dictionary<IMetadataTokenProvider, MethodReference>();
                var importedTypes = new Dictionary<IMetadataTokenProvider, TypeReference>();

                foreach (var type in assembly.Modules.SelectMany(m => m.Types))
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body == null)
                        {
                            continue;
                        }

                        var integration = FindIntegration(method);

                        if (integration == null)
                        {
                            continue;
                        }

                        Console.WriteLine($"Patching {integration.Value.TargetType}.{integration.Value.TargetMethod} in {integration.Value.TargetAssembly}");

                        var ilProcessor = method.Body.GetILProcessor();

                        if (!importedTypes.TryGetValue(callTargetStateType, out var callTargetStateReference))
                        {
                            callTargetStateReference = assembly.MainModule.ImportReference(callTargetStateType);
                        }

                        var stateVariable = new VariableDefinition(callTargetStateReference);
                        ilProcessor.Body.Variables.Add(stateVariable);

                        var callTargetBegin = callTargetInvokerType.Methods
                            .First(m => m.Name == "BeginMethod"
                                && m.GenericParameters.Count == 2 + method.Parameters.Count
                                && m.Parameters.Count == m.GenericParameters.Count - 1
                                && (method.Parameters.Count == 0 || m.Parameters[1].ParameterType.IsByReference));

                        if (!importedMethods.TryGetValue(callTargetBegin, out var callTargetBeginReference))
                        {
                            callTargetBeginReference = assembly.MainModule.ImportReference(callTargetBegin);
                        }

                        var genericCallTargetBegin = new GenericInstanceMethod(callTargetBeginReference);

                        var integrationType = tracerAssembly.MainModule.GetType(integration.Value.IntegrationType);

                        var integrationTypeReference = assembly.MainModule.ImportReference(integrationType);

                        genericCallTargetBegin.GenericArguments.Add(integrationTypeReference);
                        genericCallTargetBegin.GenericArguments.Add(method.DeclaringType);

                        foreach (var arg in method.Parameters)
                        {
                            genericCallTargetBegin.GenericArguments.Add(arg.ParameterType);
                        }

                        var start = ilProcessor.Body.Instructions[0];

                        ilProcessor.InsertBefore(start, Instruction.Create(OpCodes.Ldarg_0));

                        for (int i = 0; i < method.Parameters.Count; i++)
                        {
                            ilProcessor.InsertBefore(start, Instruction.Create(OpCodes.Ldarga, method.Parameters[i]));
                        }

                        ilProcessor.InsertBefore(start, Instruction.Create(OpCodes.Call, genericCallTargetBegin));
                        ilProcessor.InsertBefore(start, Instruction.Create(OpCodes.Stloc, stateVariable));

                        var ldstr = Instruction.Create(OpCodes.Ldstr, "Patched");

                        ilProcessor.InsertBefore(ilProcessor.Body.Instructions[0], ldstr);
                        ilProcessor.InsertAfter(ldstr, Instruction.Create(OpCodes.Call, writeLineMethodReference));

                        var callTargetEnd = callTargetInvokerType.Methods
                            .First(m => m.Name == "EndMethod"
                                && m.Parameters.Count == (method.ReturnType == null ? 3 : 4)
                                && m.Parameters.Last().ParameterType.IsByReference);

                        if (!importedMethods.TryGetValue(callTargetEnd, out var callTargetEndReference))
                        {
                            callTargetEndReference = assembly.MainModule.ImportReference(callTargetEnd);
                        }

                        var genericCallTargetEnd = new GenericInstanceMethod(callTargetEndReference);
                        genericCallTargetEnd.GenericArguments.Add(integrationTypeReference);
                        genericCallTargetEnd.GenericArguments.Add(method.DeclaringType);

                        if (method.ReturnType != null)
                        {
                            genericCallTargetEnd.GenericArguments.Add(method.ReturnType);
                        }

                        var returnVariable = new VariableDefinition(method.ReturnType);
                        ilProcessor.Body.Variables.Add(returnVariable);

                        var exit = Instruction.Create(OpCodes.Ldarg_0);

                        ilProcessor.Append(exit);
                        ilProcessor.Emit(OpCodes.Ldloc, returnVariable);
                        ilProcessor.Emit(OpCodes.Ldnull);
                        ilProcessor.Emit(OpCodes.Ldloca, stateVariable);

                        ilProcessor.Emit(OpCodes.Call, genericCallTargetEnd);

                        if (method.ReturnType == null)
                        {
                            ilProcessor.Emit(OpCodes.Pop);
                        }
                        else
                        {
                            if (!importedTypes.TryGetValue(callTargetReturnType, out var callTargetReturnTypeReference))
                            {
                                callTargetReturnTypeReference = assembly.MainModule.ImportReference(callTargetReturnType);
                            }

                            var callTargetReturnTypeGeneric = new GenericInstanceType(callTargetReturnTypeReference);
                            callTargetReturnTypeGeneric.GenericArguments.Add(method.ReturnType);

                            if (!importedTypes.TryGetValue(callTargetReturnTypeGeneric, out var callTargetReturnTypeGenericReference))
                            {
                                callTargetReturnTypeGenericReference = assembly.MainModule.ImportReference(callTargetReturnTypeGeneric);
                            }

                            var callTargetReturnVariable = new VariableDefinition(callTargetReturnTypeGeneric);
                            ilProcessor.Body.Variables.Add(callTargetReturnVariable);

                            ilProcessor.Emit(OpCodes.Stloc, callTargetReturnVariable);
                            ilProcessor.Emit(OpCodes.Ldloca, callTargetReturnVariable);

                            var callTargetReturnMethod = callTargetReturnTypeGenericReference.Resolve().Methods.First(t => t.Name == "GetReturnValue");

                            if (!importedMethods.TryGetValue(callTargetReturnMethod, out var callTargetReturnMethodReference))
                            {
                                callTargetReturnMethodReference = assembly.MainModule.ImportReference(callTargetReturnMethod);
                            }

                            ilProcessor.Emit(OpCodes.Call, callTargetReturnMethodReference);
                        }

                        var ret = Instruction.Create(OpCodes.Ret);
                        ilProcessor.Append(ret);

                        foreach (var instruction in method.Body.Instructions.ToList())
                        {
                            if (instruction.OpCode == OpCodes.Ret && instruction != ret)
                            {
                                if (method.ReturnType != null)
                                {
                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Stloc, returnVariable));
                                }

                                ilProcessor.Replace(instruction, Instruction.Create(OpCodes.Br_S, exit));
                            }
                        }

                        isAssemblyModified = true;
                    }
                }

                if (isAssemblyModified)
                {
                    Console.WriteLine($"Saving assembly {assembly.Name}");
                    assembly.Write(Path.Combine(outputFolder, Path.GetFileName(file)));
                }
            }

            File.Copy(tracerPath, Path.Combine(outputFolder, "Datadog.Trace.dll"), overwrite: true);

            Console.WriteLine("Patching deps.json file");

            var depsJsonPath = Path.Combine(outputFolder, depsJson);

            var json = JObject.Parse(File.ReadAllText(depsJsonPath));

            var libraries = (JObject)json["libraries"];

            libraries.Add("Datadog.Trace/2.1.0.0", JObject.FromObject(new
            {
                type = "reference",
                serviceable = false,
                sha512 = ""
            }));

            var targets = (JObject)json["targets"];

            foreach (var targetProperty in targets.Properties())
            {
                var target = (JObject)targetProperty.Value;

                target.Add("Datadog.Trace/2.1.0.0", new JObject(new JProperty("runtime", new JObject(
                        new JProperty("Datadog.Trace.dll", new JObject(
                            new JProperty("assemblyVersion", "2.1.0.0"),
                            new JProperty("fileVersion", "2.1.0.0")))))));
            }

            using (var stream = File.CreateText(depsJsonPath))
            {
                using (var writer = new JsonTextWriter(stream) { Formatting = Formatting.Indented })
                {
                    json.WriteTo(writer);
                }
            }

            Console.WriteLine("Done");
        }

        private static bool InjectStartupHook(AssemblyDefinition assembly, AssemblyDefinition tracerAssembly)
        {
            if (assembly.EntryPoint == null)
            {
                Console.WriteLine($"Could not locate entry point of assembly {assembly.Name}");
                return false;
            }

            var initializeMethod = tracerAssembly.MainModule.Types.First(t => t.FullName == "Datadog.Trace.ClrProfiler.Instrumentation")
                .Methods.First(t => t.Name == "Initialize");

            var initializeMethodReference = assembly.MainModule.ImportReference(initializeMethod);

            var ilProcessor = assembly.EntryPoint.Body.GetILProcessor();




            ilProcessor.InsertBefore(ilProcessor.Body.Instructions[0], Instruction.Create(OpCodes.Call, initializeMethodReference));

            return true;
        }

        private static NativeCallTargetDefinition[] ReadIntegrations(string path)
        {
            var assembly = Assembly.LoadFrom(path);

            var instrumentationDefinitions = assembly.GetType("Datadog.Trace.ClrProfiler.InstrumentationDefinitions");
            var method = instrumentationDefinitions.GetMethod("GetDefinitionsArray", BindingFlags.NonPublic | BindingFlags.Static);

            var array = (Array)method.Invoke(null, null);

            var elementType = array.GetType().GetElementType();
            var disposeMethod = elementType.GetMethod("Dispose");

            var definitionType = typeof(NativeCallTargetDefinition);

            var result = new NativeCallTargetDefinition[array.Length];

            IntPtr signaturesPtr = default;

            for (int i = 0; i < array.Length; i++)
            {
                var boxedDefinition = (object)default(NativeCallTargetDefinition);

                var obj = array.GetValue(i);

                foreach (var field in elementType.GetFields())
                {
                    if (field.Name == "TargetSignatureTypes")
                    {
                        signaturesPtr = (IntPtr)field.GetValue(obj);
                        continue;
                    }

                    definitionType.GetField(field.Name).SetValue(boxedDefinition, field.GetValue(obj));
                }

                var length = ((NativeCallTargetDefinition)boxedDefinition).TargetSignatureTypesLength;

                var targetSignatureTypes = new string[length];

                for (int j = 0; j < length; j++)
                {
                    var ptr = Marshal.ReadIntPtr(signaturesPtr + Marshal.SizeOf<IntPtr>() * j);
                    targetSignatureTypes[j] = Marshal.PtrToStringUni(ptr);
                }

                disposeMethod.Invoke(obj, null);

                result[i] = (NativeCallTargetDefinition)boxedDefinition;
                result[i].TargetSignatureTypes = targetSignatureTypes;
            }

            return result;
        }

        private static NativeCallTargetDefinition? FindIntegration(MethodDefinition method)
        {
            var assembly = method.Module.Assembly;

            foreach (var integration in _integrations)
            {
                if (integration.TargetAssembly != assembly.Name.Name)
                {
                    continue;
                }

                if (integration.TargetMaximumMajor < assembly.Name.Version.Major
                    || integration.TargetMaximumMinor < assembly.Name.Version.Minor
                    || integration.TargetMaximumPatch < assembly.Name.Version.Revision)
                {
                    continue;
                }

                if (integration.TargetMinimumMajor > assembly.Name.Version.Major
                    || integration.TargetMinimumMinor < assembly.Name.Version.Minor
                    || integration.TargetMinimumPatch < assembly.Name.Version.Revision)
                {
                    continue;
                }

                if (integration.TargetMethod != method.Name)
                {
                    continue;
                }

                if (integration.TargetType != method.DeclaringType.FullName)
                {
                    continue;
                }

                var parametersCount = method.Parameters.Count;

                if (method.ReturnType != null)
                {
                    parametersCount++;
                }

                if (integration.TargetSignatureTypesLength != parametersCount)
                {
                    continue;
                }

                return integration;
            }

            return null;
        }
    }
}
