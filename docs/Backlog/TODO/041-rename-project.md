# 041 — Rename the Project

**Status:** TODO
**Sequence:** 41
**Dependencies:** None

---

## Summary

The project is currently called **MattGPT**, which is a poor name for several reasons: the "Matt" in the name is too personal and doesn't generalise to other users, other public projects already use the same name, and the name doesn't reflect what the application actually does. This issue tracks renaming the project to something that better describes its purpose and can serve as a credible public identity.

## Background

The core value proposition of this application is: import your ChatGPT (or other LLM chat) history and make it available as searchable RAG memory for any LLM — essentially a long-term, searchable, conversational memory layer. The name should communicate that idea.

This is a prerequisite for publishing a Docker image to a public registry (issue 042), since the image tag and registry namespace depend on the chosen name.

## Requirements

1. Agree on a new name for the project. The name should:
   - Be available as a Docker Hub / GitHub Container Registry image name.
   - Not already be in wide use by another open-source project.
   - Reflect the "personal memory / RAG / chat history" concept.
   - Be short, memorable, and easy to type.

2. Rename all solution, project, assembly, and namespace references from `MattGPT` to the new name across:
   - Solution file (`MattGPT.slnx`)
   - All `.csproj` files and their output/assembly names
   - All C# namespaces and `using` statements
   - Docker / Aspire project references
   - `appsettings.json` and other config files where the name appears
   - All documentation files under `docs/` and `src/`
   - The `README.md`
   - Any CI/CD workflow files

3. Update the GitHub repository name if possible (done manually by the owner — note this in the PR).

4. The rename should be a single, clean commit (or small series of commits) to make history easy to follow.

## Acceptance Criteria

- [ ] A new name is chosen and documented in this issue (update the "Chosen Name" section below).
- [ ] All solution, project, and namespace references use the new name.
- [ ] All documentation refers to the new name.
- [ ] `dotnet build MattGPT.slnx` (or equivalent renamed solution file) succeeds.
- [ ] `dotnet test` passes with zero failures.
- [ ] The Aspire AppHost starts successfully under the new name.
- [ ] A note is left for the owner to rename the GitHub repository.

## Chosen Name

*(To be decided — update this section before starting implementation.)*

### Name candidates

Some ideas to consider:

- **Recall** — "total recall", personal memory recall
- **Mnemo** / **Mnemosyne** — Greek goddess of memory and mother of the Muses
- **Anamnesis** — Greek for "unforgetting" / recollection
- **Palimpsest** — a document where old writing shines through new — layered memory
- **Engram** — the physical trace of a memory in the brain
- **Reminisce** — straightforward "memory" connotation
- **Memex** — Vannevar Bush's 1945 vision of a personal memory extension device (public domain term)
- **Loci** — from the "method of loci" / memory palace technique

## Notes

- Check Docker Hub and `ghcr.io` availability for shortlisted names before committing.
- Check `npmjs.com` and PyPI if any tooling packages are planned in future.
- The name change does not affect the data model or APIs — it is purely a surface/branding change.
