; Unshipped analyzer release
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
ZF2011  | Markup   | Error    | No command for a command property
ZF2012  | Markup   | Error    | A style in the markup can't be read
ZF2013  | Markup   | Warning  | A style in the markup names what isn't there
ZF2014  | Markup   | Warning  | An included style sheet is not among the project's files