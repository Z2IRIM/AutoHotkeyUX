namespace AutoHotkeyUX.Modern.Models;

/// <summary>Names the closed configurable operation modes introduced by workflow version 3.</summary>
internal enum FlowConfigurationMode { Inherit, Custom }
internal enum FlowTerminalProgram { Auto, WindowsTerminal, WindowsPowerShell }
internal enum FlowTerminalPosition { Above, Below, Left, Right, Center }
internal enum FlowArchiveDestination { BesideArchive, Custom, Inherit }
internal enum FlowArchiveNaming { ArchiveName, Composition }
internal enum FlowArchiveCollision { AutoSuffix, Error }
internal enum FlowMissingTarget { StopSilently, Error }

/// <summary>Snapshots terminal operation settings separately from input and shortcut enablement.</summary>
internal sealed record FlowTerminalOptions
{
    public FlowConfigurationMode Mode { get; init; }
    public FlowTerminalProgram Program { get; init; }
    public FlowTerminalPosition Position { get; init; }
    public int Width { get; init; } = 800;
    public int Height { get; init; } = 440;
    public int Gap { get; init; } = 12;
    public bool WaitReady { get; init; } = true;
    public int TimeoutMs { get; init; } = 4000;
}

/// <summary>Defines per-archive destination, safe naming and non-overwriting commit policy.</summary>
internal sealed record FlowExtractionOptions
{
    public FlowConfigurationMode Mode { get; init; }
    public FlowArchiveDestination Destination { get; init; }
    public FlowArchiveNaming Naming { get; init; }
    public FlowTextExpression Name { get; init; } = new();
    public FlowArchiveCollision Collision { get; init; }
}

/// <summary>Combines bounded literal text and typed results without executable expressions.</summary>
internal sealed record FlowTextExpression
{
    public FlowInput[] Parts { get; init; } = [];
}

/// <summary>Specifies relative path segments independently of the absolute base directory.</summary>
internal sealed record FlowJoinPathOptions
{
    public FlowInput[] Segments { get; init; } = [];
}

/// <summary>Controls missing-target handling while preserving mouse hit and drag rules.</summary>
internal sealed record FlowContextOptions
{
    public FlowMissingTarget Missing { get; init; }
}

/// <summary>Creates a directory idempotently or explicitly rejects an existing directory.</summary>
internal sealed record FlowDirectoryOptions
{
    public bool FailIfExists { get; init; }
}

/// <summary>Stops one invocation with an observable reason and optional notification.</summary>
internal sealed record FlowStopOptions
{
    public string Reason { get; init; } = "No supported target.";
    public bool Notify { get; init; }
}

/// <summary>Builds notification content from the same typed text composition model.</summary>
internal sealed record FlowNotificationOptions
{
    public FlowTextExpression Title { get; init; } = new() { Parts = [new() { Literal = "AutoHotkey workflow" }] };
    public FlowTextExpression Message { get; init; } = new() { Parts = [new() { Literal = "Completed." }] };
}

/// <summary>Defines node-local error visibility; failure always stops dependent actions.</summary>
internal sealed record FlowFailureOptions
{
    public bool Notify { get; init; }
}
