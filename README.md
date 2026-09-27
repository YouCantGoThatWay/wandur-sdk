# Wandur SDK

Shared .NET libraries behind the [Wandur](https://github.com/YouCantGoThatWay/wandur)
MUD client and the services around it. Two projects, no framework dependencies,
no GUI, no HTTP client, no database and no AI SDK.

| Project | Responsibility |
| --- | --- |
| `Wandur.Models` | Game-neutral state, protocol mapping contracts, evidence fingerprints and validation |
| `Wandur.Protocol` | Telnet negotiation, GMCP and MSDP decoding, room protocol decoding and protocol diagnostics |

`Wandur.Models` owns normalized game state (character, opponent, vehicle and
world entities, resources with separate current and maximum observations,
progression, attributes, currencies, metrics and location collections), the
`WorldMapping` contract that binds exact protocol sources to constrained
entity/category/key/member targets, `ProtocolEvidence`, and the validation that
keeps all of it inside its declared bounds.

`Wandur.Protocol` owns the wire side: the Telnet parser and option negotiation,
GMCP and MSDP decoding, GMCP login, room protocol decoding, and the diagnostic
formatter and redactor that make received protocol traffic readable without
leaking credentials.

The Telnet parser takes an optional `TelnetParserOptions`; `new TelnetParser()`
uses defaults that match the desktop client. It answers TTYPE with the MTTS
cycle (client name, terminal type, then `MTTS <bitvector>` from
`MttsCapabilities`), reports the NAWS window size and returns an update from
`UpdateWindowSize` once NAWS is agreed, accepts EOR and reports every IAC GA and
IAC EOR in `TelnetPacket.PromptMarks` at its offset in the text, and, when
`AcceptMssp` is on, accepts MSSP and delivers each block as an `MsspTable` in
`TelnetPacket.Mssp`.

The default MTTS capabilities are ANSI, VT100, 256 colors and truecolor. They
do not claim UTF-8: set `MttsCapabilities.Utf8` in `Capabilities` when the
connection is decoded as UTF-8, and `MttsCapabilities.Ssl` on TLS.

Both are game-neutral. Named keys are extensible: `health`, `jetpack_fuel`,
`engineering` or another game's vocabulary all validate the same way.

## Use it as a submodule

NuGet publishing is prepared but not active yet: no packages are on NuGet.org.
Until they are, consumers take this repository as a git submodule at
`external/wandur-sdk` and reference the projects directly.

```sh
git submodule add https://github.com/YouCantGoThatWay/wandur-sdk.git external/wandur-sdk
git submodule update --init --recursive
```

Then reference the projects from a consuming `.csproj`:

```xml
<ItemGroup>
  <ProjectReference Include="../../external/wandur-sdk/Wandur.Models/Wandur.Models.csproj" />
  <ProjectReference Include="../../external/wandur-sdk/Wandur.Protocol/Wandur.Protocol.csproj" />
</ItemGroup>
```

Clone a consumer with `git clone --recurse-submodules`, and check out with
`submodules: recursive` in CI.

`Wandur.Protocol` exposes its internals to `Wandur.Core` and `Wandur.Core.Tests`
through `InternalsVisibleTo`, so the client's core library can use the parser
internals it has always used.

## Build

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), then:

```sh
dotnet restore Wandur.Sdk.sln
dotnet build Wandur.Sdk.sln -c Release
```

`TreatWarningsAsErrors` is on and lock files are committed, so CI restores with
`--locked-mode`.

## Test

```sh
dotnet test Wandur.Sdk.sln
```

`Wandur.Protocol.Tests` covers the Telnet parser: its baseline behaviour, MTTS,
NAWS, GA and EOR prompt marks, and MSSP. CI runs it after the build. The
client's `Wandur.Core.Tests` project still covers the rest of the protocol code
through `InternalsVisibleTo`.

## Packaging

`.github/workflows/publish-packages.yml` packs `Wandur.Models` and
`Wandur.Protocol` and pushes them to NuGet.org on a `v*` tag, with the tag
supplying the version. It is inert until the repository secret `NUGET_API_KEY`
holds a NuGet.org key scoped to those two package ids.

## License

MIT. See [LICENSE](LICENSE).
