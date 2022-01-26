namespace DatadogAssemblyPatcher;

internal struct NativeCallTargetDefinition
{
    public string TargetAssembly;

    public string TargetType;

    public string TargetMethod;

    public string[] TargetSignatureTypes;

    public ushort TargetSignatureTypesLength;

    public ushort TargetMinimumMajor;

    public ushort TargetMinimumMinor;

    public ushort TargetMinimumPatch;

    public ushort TargetMaximumMajor;

    public ushort TargetMaximumMinor;

    public ushort TargetMaximumPatch;

    public string IntegrationAssembly;

    public string IntegrationType;
}