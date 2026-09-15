using System.Runtime.Versioning;

// Devlog.Api's own TargetFramework is plain net10.0 (not net10.0-windows) so
// that the Contracts/ folder alone could one day be referenced by a
// cross-platform cloud server without dragging this attribute along - but the
// rest of the assembly (AiKeyStore's DPAPI use, and everything that touches
// it) is Windows-only in practice, same as every other project in this
// solution but Devlog.Core. Declaring it here once avoids chasing CA1416
// through every call site.
[assembly: SupportedOSPlatform("windows")]
