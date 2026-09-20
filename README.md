# JsonGoddess
Performant &amp; Allocation free C# serializer+deserializer built on top of incremental source generators.

## Where it makes a difference, and where it does not

Ratios below are shares of a whole HTTP request, measured against `System.Text.Json` doing the
same work in the same benchmark run. Anything above a few hundred kilobytes is where they show;
below that, JSON is not what a request spends its time on, and no serializer changes that.

| | switch it on with | share of the request |
|---|---|--:|
| MVC — writing the response | `o.JsonSerializerOptions.UseJsonGoddess()` | **0.46** |
| MVC — reading the request body | `AddControllers(o => o.AddJsonGoddess())` | **0.77**, and **0.55** on 3.7 MB with Gen2 281 → 0 |
| minimal API — writing the response | `o.SerializerOptions.UseJsonGoddess()` | **0.45** |
| minimal API — reading the request body | — **not accelerated** | 0.96, which is nothing |

### Why minimal API request bodies are not accelerated

Not an oversight, and not something the next release fixes on our side.

Plugging into `JsonSerializerOptions` means handing `System.Text.Json` a converter, and while a
body is still arriving it will not call a third-party converter without first proving the value is
fully buffered — it `Skip`s the value, then reads it again. That toll is the whole win, and it is
charged in MVC too. MVC has a second door: an input formatter, which reads the body itself and
never enters `System.Text.Json` at all. That is what `AddJsonGoddess()` installs, and where the
0.77 and 0.55 come from.

Minimal API has no such door. It does not use input formatters, and ASP.NET Core has no general
hook for replacing parameter binding — the requests for one are open and unshipped
([#35489](https://github.com/dotnet/aspnetcore/issues/35489),
[#50672](https://github.com/dotnet/aspnetcore/issues/50672)).

### What you can do today

Bind the body as a `PipeReader` — a supported, first-class minimal API parameter — and read it with
the generated reader. No `System.Text.Json` on this path at all.

Two things in this recipe are not decoration, and leaving either out gives you an endpoint that
quietly disagrees with `System.Text.Json`. Both were established by running it, not by reasoning:

```csharp
// Guards are opt-in, and this is the set the bridge installs for ASP.NET.
// Without StrictNumbers, [{"id":01}] is accepted here and rejected by
// System.Text.Json - a difference you would find in production, not in a test.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
[JsonFeature(JsonFeature.CaseInsensitiveNames)]
[JsonGuard(
    JsonGuard.TrailingContent
    | JsonGuard.ControlCharsInStrings
    | JsonGuard.StrictNumbers
    | JsonGuard.InvalidUtf8
    | JsonGuard.MaxDepth,
    MaxDepth = 64
    )]
[JsonSubject(typeof(Order), true)]
internal partial class MyHost
{
}

app.MapPost("/orders", async (PipeReader body) =>
{
    try
    {
        var orders = await MyHost.StreamReadArray_MyApp_Order(DefaultInjector.Instance, body);

        return Results.Ok(orders.Length);
    }
    // Nothing answers 400 for you here any more: an escaping exception is a 500.
    catch (JsonDocumentException failure)
    {
        return Results.Problem(failure.Message, statusCode: StatusCodes.Status400BadRequest);
    }
});
```

With both in place, a truncated body and a leading zero answer `400`, exactly as the stock endpoint
does, and a good body answers `200`.

What you still give up: the endpoint no longer declares that it accepts JSON, so OpenAPI will not
say so, and endpoint filters no longer see a bound argument.
