; Shipped analyzer releases
; https://github.com/dotnet/roslyn/blob/main/src/RoslynAnalyzers/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

## Release 0.15.0

### New Rules

Rule ID | Category    | Severity | Notes
--------|-------------|----------|-------
ZF0001  | Reliability | Warning  | Assigning a new ObservableCollection instance
ZF0002  | Design      | Warning  | An overload is shadowed by another with the same required parameters
ZF0003  | Design      | Error    | A property with [Styled] is not declared partial
ZF0004  | Design      | Error    | A type with [Styled] properties is not declared partial
ZF0005  | Design      | Error    | [Styled] on a type that doesn't derive from UIElement
ZF0006  | Design      | Warning  | Assigning a styled property in a control's constructor
ZF2001  | Markup      | Error    | The markup is not well-formed XML
ZF2002  | Markup      | Error    | The markup names no class
ZF2003  | Markup      | Error    | Unknown element
ZF2004  | Markup      | Error    | Unknown property or event
ZF2005  | Markup      | Error    | A value can't be written
ZF2006  | Markup      | Error    | A child has nowhere to go
ZF2007  | Markup      | Error    | No handler for an event
ZF2008  | Markup      | Error    | A binding can't be made
ZF2009  | Markup      | Error    | A name is used twice
ZF2010  | Markup      | Error    | An element can't be created
ZF2011  | Markup      | Error    | No command for a command property
ZF2012  | Markup      | Error    | A style in the markup can't be read
ZF2013  | Markup      | Warning  | A style in the markup names what isn't there
ZF2014  | Markup      | Warning  | An included style sheet is not among the project's files
