# Public API and releases

## The public API files

`src/ZeppelinForms` lists its public API in two files, checked on every build by
`Microsoft.CodeAnalysis.PublicApiAnalyzers`:

| File | Holds |
| ---- | ----- |
| `PublicAPI.Shipped.txt` | The API of the versions already on NuGet |
| `PublicAPI.Unshipped.txt` | What the version being made adds or removes |

A public member that is in neither file fails the build with **RS0016**; a line for a member
that no longer exists, with **RS0017**. A change to a shipped member — a return type, a
nullability — is a removal and an addition: a `*REMOVED*` line with the old signature, and the
new one.

```
#nullable enable
*REMOVED*ZeppelinForms.Forms.Form.Font.get -> ZeppelinForms.Drawing.Font?
ZeppelinForms.Forms.Form.Font.get -> ZeppelinForms.Drawing.Font!
```

The analyzer also guards compatibility: two overloads that both have optional parameters
(**RS0026**), or an overload with more parameters than the one with the optional ones
(**RS0027**), would be ambiguous to code compiled against either — give the new method a name
of its own instead.

### When a version ships

Run once at the start of every new release branch, right after the previous version went out:

```
dotnet run tools/ship-api.cs              # ship every project of the repository
dotnet run tools/ship-api.cs -- --check   # only report, change nothing
```

Every ordinary line of Unshipped moves into Shipped, every `*REMOVED*` line deletes its target
from Shipped, and Unshipped is left with its `#nullable enable` header. A `*REMOVED*` line
whose target isn't in Shipped stops the tool without changing anything: Shipped is out of
date, and shipping on top of that would hide it.

So the files are never cleared by hand: Unshipped grows during a release and is emptied by the
tool when the next one begins.

## Breaking changes

A release lists them first in the
[changelog](https://github.com/YD359Team/ZeppelinForms-solution/blob/master/CHANGELOG.md),
with what to do instead. While the version is 0.x, a minor version may break the API; a patch
version doesn't.

## Releasing

1. The release section of `CHANGELOG.md`: `## [0.17.0] - Codename`.
2. Merge the release branch into `master`.
3. Tag it: `git tag v0.17.0 && git push origin v0.17.0`.

The tag starts `.github/workflows/release.yml`: it runs the tests, packs the NuGet packages with
the version from the tag, publishes them to nuget.org by trusted publishing and to GitHub
Packages, and makes the GitHub release with the changelog's section as its notes and the
Visual Studio extension attached. A tag like `v0.17.0-preview.1` is a prerelease of 0.17.0
with 0.17.0's notes.

## The wiki

This wiki is `docs/wiki` in the repository. `.github/workflows/wiki.yml` publishes it on every
push to `master` that changes it, and by hand (Actions › Wiki › Run workflow). Edit the files
in the repository: an edit made in the wiki is overwritten by the next publish.
