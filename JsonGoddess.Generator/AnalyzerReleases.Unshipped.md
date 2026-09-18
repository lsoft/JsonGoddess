; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
JGD001 | JsonGoddess | Info | Call to the JsonGoddess.Compat facade keeps working through System.Text.Json instead of generated code
JGD002 | JsonGoddess | Warning | Compat generation failed after the type-graph walk had accepted the type
JGD010 | JsonGoddess | Warning | Sink type registered with [JsonExhauster]/[JsonInjector] is not sealed, so calls cannot be devirtualized
JGD020 | JsonGoddess | Error | Type carrying [JsonSubject] is not declared partial
JGD021 | JsonGoddess | Error | Subject type cannot be served
JGD022 | JsonGoddess | Error | Member type cannot be served
JGD023 | JsonGoddess | Error | Two members map to the same JSON property name
JGD024 | JsonGoddess | Error | Registered sink type does not derive from ExhausterBase/InjectorBase
JGD025 | JsonGoddess | Error | Host type shape is not supported (nested, generic or static)
JGD026 | JsonGoddess | Error | Project language version is below C# 11
JGD027 | JsonGoddess | Error | JSON property name requires escaping
JGD028 | JsonGoddess | Error | Option on [JsonSourceGenerationOptions] is not supported
JGD029 | JsonGoddess | Error | [JsonGuard] MaxDepth value is less than 1
JGD030 | JsonGoddess | Error | [JsonFeature] CaseInsensitiveNames cannot serve a non-ASCII or colliding member name
JGD031 | JsonGoddess | Error | [JsonFactory] names a type that is not a subject of this host, repeats a type, carries an empty expression, or collides with a deserialization constructor
