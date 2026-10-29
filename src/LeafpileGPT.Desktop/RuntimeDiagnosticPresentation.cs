using LeafpileGPT.Core;

namespace LeafpileGPT.Desktop;

public enum RuntimeDiagnosticPresentationKind
{
    Hidden,
    WarningStatus,
    BlockingDialog
}

public static class RuntimeDiagnosticPresentation
{
    public static RuntimeDiagnosticPresentationKind Resolve(ZMachineRuntimeErrorRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (!record.ShouldNotify)
        {
            return RuntimeDiagnosticPresentationKind.Hidden;
        }

        return record.Severity == ZMachineRuntimeErrorSeverity.Blocking
            ? RuntimeDiagnosticPresentationKind.BlockingDialog
            : RuntimeDiagnosticPresentationKind.WarningStatus;
    }
}
