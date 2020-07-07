using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using Newtonsoft.Json;

namespace DatadogAssemblyPatcher
{
    class Program
    {
        private static Integrations[] _integrations;

        static void Main(string[] args)
        {
            if (args.Length != 3)
            {
                Console.WriteLine("Usage: {inputFolder} {outputFolder} {Tracer home folder}");
                return;
            }

            var inputFolder = args[0];
            var outputFolder = args[1];
            var tracerHomeFolder = args[2];

            var integrationsAssembly = AssemblyDefinition.ReadAssembly(Path.Combine(tracerHomeFolder, "netstandard2.0", "Datadog.Trace.ClrProfiler.Managed.dll"));

            var rawJson = File.ReadAllText(Path.Combine(tracerHomeFolder, "integrations.json"));

            _integrations = JsonConvert.DeserializeObject<Integrations[]>(rawJson);

            foreach (var file in Directory.GetFiles(inputFolder, "*.dll"))
            {
                AssemblyDefinition assembly;

                try
                {
                    assembly = AssemblyDefinition.ReadAssembly(file);
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

                foreach (var type in assembly.Modules.SelectMany(m => m.Types))
                {
                    foreach (var method in type.Methods)
                    {
                        if (method.Body == null)
                        {
                            continue;
                        }

                        bool changedInstruction;

                        do
                        {
                            changedInstruction = false;

                            foreach (var instruction in method.Body.Instructions)
                            {
                                if (instruction.OpCode == OpCodes.Calli || instruction.OpCode == OpCodes.Call)
                                {
                                    var methodReference = (MethodReference)instruction.Operand;

                                    var replacement = FindReplacement(methodReference);

                                    if (replacement == null)
                                    {
                                        continue;
                                    }

                                    Console.WriteLine($"Patching call from {method.FullName} to {replacement.Target.Assembly}.{replacement.Target.Method} with {replacement.Wrapper.Type}.{replacement.Wrapper.Method}");

                                    var tempAssemblyFile = Path.GetTempFileName();

                                    int mdToken;

                                    try
                                    {
                                        assembly.Write(tempAssemblyFile);

                                        using (var tempAssembly = AssemblyDefinition.ReadAssembly(file))
                                        {
                                            var reference = tempAssembly.Modules[0].MetadataResolver.Resolve(methodReference);
                                            mdToken = reference.MetadataToken.ToInt32();
                                        }
                                    }
                                    finally
                                    {
                                        File.Delete(tempAssemblyFile);
                                    }

                                    var replacementType = integrationsAssembly.MainModule.GetType(replacement.Wrapper.Type);

                                    var replacementMethod = replacementType.Methods.First(m => m.Name == replacement.Wrapper.Method);

                                    var referenceMethod = method.Module.ImportReference(replacementMethod);
                                    var guid = method.Module.ImportReference(typeof(Guid));
                                    var ilProcessor = method.Body.GetILProcessor();

                                    if (methodReference.Parameters.Count > 0 && methodReference.Parameters.Last().ParameterType.Name == "CancellationToken")
                                    {
                                        ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Box, methodReference.Parameters.Last().ParameterType));
                                    }

                                    var mvid = methodReference.Module.Mvid;
                                    var guidVariable = new VariableDefinition(guid);

                                    method.Body.Variables.Add(guidVariable);

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4, instruction.OpCode.Value));
                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4, mdToken));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldloca_S, guidVariable));

                                    //var bytes = mvid.ToByteArray();

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                    (int)typeof(Guid).GetField("_a", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)typeof(Guid).GetField("_b", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)typeof(Guid).GetField("_c", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)(byte)typeof(Guid).GetField("_d", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)(byte)typeof(Guid).GetField("_e", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)(byte)typeof(Guid).GetField("_f", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4,
                                        (short)(byte)typeof(Guid).GetField("_g", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4_S,
                                        (sbyte)(byte)typeof(Guid).GetField("_h", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4_S,
                                        (sbyte)(byte)typeof(Guid).GetField("_i", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4_S,
                                        (sbyte)(byte)typeof(Guid).GetField("_j", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4_S,
                                        (sbyte)(byte)typeof(Guid).GetField("_k", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(mvid)));


                                    //for (int i = 0; i < 7; i++)
                                    //{
                                    //    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4, (int)bytes[i]));
                                    //}

                                    //for (int i = 0; i < 4; i++)
                                    //{
                                    //    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4_S, (sbyte)bytes[i + 7]));
                                    //}


                                    //ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldc_I4, 0));

                                    var guidConstructor = guid.Resolve().GetConstructors().First(c => c.Parameters.Count == 11);

                                    var guidConstructorRef = method.Module.ImportReference(guidConstructor);

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Call, guidConstructorRef));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Ldloca_S, guidVariable));

                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Conv_U));
                                    ilProcessor.InsertBefore(instruction, Instruction.Create(OpCodes.Conv_U8));

                                    ilProcessor.Replace(instruction, Instruction.Create(instruction.OpCode, referenceMethod));

                                    isAssemblyModified = true;
                                    changedInstruction = true;

                                    break;
                                }
                            }
                        } while (changedInstruction);
                    }
                }

                if (isAssemblyModified)
                {
                    assembly.Write(Path.Combine(outputFolder, Path.GetFileName(file)));
                }
            }

            Console.WriteLine("Done");
        }

        private static MethodReplacement FindReplacement(MethodReference candidate)
        {
            foreach (var integration in _integrations)
            {
                foreach (var replacement in integration.MethodReplacements)
                {
                    var target = replacement.Target;

                    if (target.Type != candidate.DeclaringType.FullName)
                    {
                        continue;
                    }

                    // Look for method
                    if (candidate.Name != target.Method)
                    {
                        continue;
                    }

                    int expectedParameterCount = target.SignatureTypes.Length - 1;

                    if (candidate.Parameters.Count != expectedParameterCount)
                    {
                        continue;
                    }

                    bool parameterMismatch = false;

                    for (int i = 0; i < candidate.Parameters.Count; i++)
                    {
                        if (candidate.Parameters[i].ParameterType.FullName != target.SignatureTypes[i + 1])
                        {
                            parameterMismatch = true;
                            break;
                        }
                    }

                    if (parameterMismatch)
                    {
                        continue;
                    }

                    if (candidate.ReturnType.FullName != target.SignatureTypes[0])
                    {
                        continue;
                    }

                    return replacement;
                }
            }

            return null;
        }
    }
}
