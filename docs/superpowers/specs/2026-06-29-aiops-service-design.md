# AIOpsService design

## Goal

Add an `AIOpsService` in `SmartOps.Web.Services` that exposes `Task<string> ExecutePromptAsync(string userPrompt)` and delegates prompt execution to Semantic Kernel using a DI-provided `Kernel`.

## Current context

- `SmartOps.Web\SmartOps.Web.csproj` targets `net9.0` and already references `Microsoft.SemanticKernel` and `Microsoft.SemanticKernel.Connectors.OpenAI`.
- `SmartOps.Web\Program.cs` currently configures Razor components and `AppDbContext`, but does not yet register Semantic Kernel application services.
- There is no existing `Services` folder or service pattern in `SmartOps.Web`, so this addition should stay minimal and align with the current lightweight composition style.

## Chosen approach

Create a thin application service that wraps `Kernel.InvokePromptAsync(...)` and is responsible only for translating a user prompt into a string response.

The service will build `PromptExecutionSettings` with `FunctionChoiceBehavior.Auto()` on every invocation so future C# plugins can be selected autonomously by the model without changing the public service contract.

## Design details

### Service shape

- Add `SmartOps.Web\Services\AIOpsService.cs`.
- Define a constructor that requires `Kernel`.
- Store the injected kernel in a private readonly field.
- Expose a single public async method:
  - `Task<string> ExecutePromptAsync(string userPrompt)`

### Prompt execution

- Inside `ExecutePromptAsync`, create a `PromptExecutionSettings` instance.
- Set `FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()`.
- Call `await _kernel.InvokePromptAsync(userPrompt, new(settings))`.
- Convert the returned result into the final string response using the Semantic Kernel result string representation.

This keeps the service ready for tool and plugin calling while avoiding any OpenAI-specific coupling in the service implementation itself.

### Dependency injection

- Register `AIOpsService` in `SmartOps.Web\Program.cs`.
- Use scoped lifetime so the service participates naturally in the ASP.NET Core request scope and matches typical application-service usage.

This registration is intentionally limited to the wrapper service. Kernel construction and OpenAI client registration remain separate infrastructure concerns and can be introduced later without changing consumers of `AIOpsService`.

### Error handling

- Do not swallow Semantic Kernel or provider exceptions.
- Invalid kernel configuration, upstream model failures, or invocation errors should bubble up so the caller can decide how to surface them.
- The method will not add silent fallback strings or broad catch blocks.

### Testing and verification

- Build `SmartOps.Web` after adding the service and DI registration.
- Confirm the service compiles against the installed Semantic Kernel API surface for the current package version.

## Scope

Included in scope:

- `SmartOps.Web\Services\AIOpsService.cs`
- `SmartOps.Web\Program.cs`

Out of scope:

- OpenAI API key or endpoint configuration
- Kernel registration and model provider wiring
- Plugin implementations
- UI or endpoint integration that consumes `AIOpsService`
