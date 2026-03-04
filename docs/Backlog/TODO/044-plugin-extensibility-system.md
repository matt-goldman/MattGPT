# 044 — Plugin System for Tool-Calling Extensibility

**Status:** TODO
**Sequence:** 44
**Dependencies:** 020 (tool-calling RAG retrieval)

---

## Summary

Implement a plugin system that allows users to extend the application's tool-calling capabilities by dropping custom .NET assemblies into a `Plugins/` directory. The system uses startup reflection to discover assemblies, identify types implementing a plugin contract, and register them as available tools for the LLM.

## Background

The existing tool-calling system (issue 020) ships with a fixed set of built-in tools (primarily `SearchMemoriesTool`). Users who want to add custom tools — for example, tools that query their own APIs, look up calendar events, or search local files — currently have no way to do this without modifying and rebuilding the source.

For users running from source this is fine: fork/clone the repo and add tools directly. But for users running from the published Docker image (issue 042), source modification is impractical. A plugin drop-in model solves this: mount a `Plugins/` volume into the container and place custom assemblies there.

## Requirements

### Plugin Contract (Design Decision Required)

The first task in this issue is to evaluate and decide how the plugin contract is defined. Two approaches exist:

**Option A — Microsoft.Extensions.AI (`AIFunction`)**
The existing tool-calling infrastructure (issue 020) uses `Microsoft.Extensions.AI`. Plugin authors would implement tools using the same `AIFunction` / `AIFunctionFactory` API that built-in tools use. Advantages: no custom abstractions to maintain, plug-ins feel native to M.E.AI, no separate NuGet package required. Disadvantages: plugin authors must take a dependency on `Microsoft.Extensions.AI`; the API surface is larger than a minimal contract.

**Option B — Custom `ToolPlugin` abstraction**
Define a dedicated abstract base class or interface (e.g. `ToolPlugin`) in a thin, purpose-built NuGet package (e.g. `[ProjectName].Plugins.Abstractions`) with no heavy dependencies. The plugin loader wraps discovered instances in an `AIFunction` adapter at registration time. Advantages: minimal surface area, stable contract independent of M.E.AI versioning, easier for users unfamiliar with M.E.AI. Disadvantages: bespoke abstraction to maintain; an extra indirection layer.

1. Evaluate both options at the start of implementation. Document the decision in an ADR before writing plugin-loading code.

2. Whichever approach is chosen, the contract must be consumable without referencing the main application project. For Option A this means documenting the `Microsoft.Extensions.AI` package reference; for Option B this means publishing (or making buildable from source) a dedicated abstractions package.

3. The chosen contract must be compatible with `net9.0` or later.

### Plugin Loader

4. At startup, scan the `Plugins/` directory (configurable path, defaulting to `./Plugins` relative to the working directory, or overridable via `Plugins:Path` config key).

5. For each `.dll` found in `Plugins/`:
   - Load the assembly using `Assembly.LoadFrom` (or `AssemblyLoadContext` for isolation).
   - Enumerate all exported types.
   - Identify concrete types that match the chosen plugin contract (see design decision above).
   - Attempt to instantiate each using the default constructor or via `IServiceProvider` (prefer DI).
   - Register each successfully instantiated plugin with the tool registry as an `AIFunction` (wrapping if necessary).

6. Log a startup message for each plugin loaded: `Loaded plugin '{Name}' from '{AssemblyPath}'`.
7. Log a warning (not an exception) for any assembly that fails to load or any type that fails to instantiate.
8. If `Plugins/` does not exist, skip silently (not an error).

### Tool Registry Integration

9. Integrate plugin-provided tools with the existing `SearchMemoriesTool` / tool-calling infrastructure so they appear in the function spec sent to the LLM alongside built-in tools.
10. Plugin tools should be indistinguishable from built-in tools from the LLM's perspective.
11. When the LLM calls a plugin tool, dispatch the call through whichever contract was chosen and return the result as a tool message.

### Allowed Tools Configuration

12. Add a `Plugins:AllowedTools` configuration key (list of tool names). When set, only tools whose `Name` matches an entry in the list are registered. When absent or empty, all discovered tools are registered.
13. This config can be set via environment variables, the `docker-compose` file, or the settings UI (issue 043 / 029).

### Security Considerations

14. Document clearly that loading arbitrary assemblies is a security risk — users should only load plugins they trust.
15. Consider adding a `Plugins:Enabled` config flag (default `false`) that must be explicitly set to `true` to activate the loader — opt-in, not opt-out.
16. Note in documentation that plugin assemblies run in the same process and have full access to the application's resources.

### Documentation

17. Write a `docs/UserGuides/plugins.md` guide covering:
    - How to write a plugin (with a minimal example).
    - How to build and deploy the plugin DLL.
    - How to mount the `Plugins/` volume in Docker.
    - Available configuration options (`Plugins:Enabled`, `Plugins:Path`, `Plugins:AllowedTools`).

## Example Plugin (Documentation / Test Fixture)

The exact shape of the example will depend on the contract decision. The documentation should include a minimal working example that a user can copy, compile, drop into `Plugins/`, and have the LLM call immediately. The example must be kept in sync with whichever API is chosen.

## Acceptance Criteria

- [ ] An ADR documents the chosen plugin contract approach (Microsoft.Extensions.AI vs. custom abstraction) and the reasons for rejecting the alternative.
- [ ] The chosen plugin contract is consumable without referencing the main application project.
- [ ] The plugin loader scans `Plugins/` at startup and registers all valid plugin types.
- [ ] Plugin tools appear alongside built-in tools in the LLM function spec.
- [ ] When the LLM invokes a plugin tool, `ExecuteAsync` is called and the result is returned correctly.
- [ ] A missing or empty `Plugins/` directory causes no error.
- [ ] A malformed or incompatible assembly causes a warning log entry but does not prevent startup.
- [ ] `Plugins:Enabled` defaults to `false`; plugins are not loaded unless explicitly opted in.
- [ ] `Plugins:AllowedTools` correctly restricts which discovered plugins are registered.
- [ ] `docs/UserGuides/plugins.md` exists and contains a working example.
- [ ] `dotnet test` passes with zero failures.

## Notes

- Consider `AssemblyLoadContext` with isolation if plugin isolation is important in future; for MVP, `Assembly.LoadFrom` is acceptable.
- If a plugin needs injected services (e.g. `HttpClient`), consider supporting constructor injection via `IServiceProvider` — plugins registered with `ActivatorUtilities.CreateInstance` get DI for free.
- This issue covers the container plugin path. The fork/clone path for source users requires no new infrastructure.
