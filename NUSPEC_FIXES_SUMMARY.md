# NuSpec Files - Refactoring Summary

## Changes Made

Both NuSpec files have been updated to reflect the correct project configuration:

### Files Updated
- ✅ `QuestDB.Change.Tracker.Api\pack.nuspec`
- ✅ `QuestDB.Change.Tracker.Api\consul-pack.nuspec`

## Details of Changes

### 1. Package Metadata

#### Before
```xml
<id>ConfigurationManager.Api</id>
<version>3.0.3.19</version>
<projectUrl>https://github.com/barimale/ConfigurationManager.Api</projectUrl>
<description>.NET Consul Client provided with eager and lazy appSettings and connectionStrings adapters</description>
<tags>Consul</tags>
```

#### After
```xml
<id>QuestDB.Change.Tracker.Api</id>
<version>1.0.0.0</version>
<projectUrl>https://github.com/barimale/QuestDB.Change.Tracker</projectUrl>
<description>.NET QuestDB Change Tracker API - WAL-based change detection and tracking for QuestDB tables with event-driven architecture</description>
<tags>QuestDB,WAL,ChangeTracking,Events,Database</tags>
```

### 2. Dependencies

#### Before
```xml
<dependencies>
	<group targetFramework=".NETCoreApp3.0">
		<dependency id="Consul" version="0.7.2.6" exclude="Build,Analyzers" />
		<dependency id="System.Configuration.ConfigurationManager" version="4.7.0" exclude="Build,Analyzers" />
	</group>
	<group targetFramework="net8.0">
		<dependency id="Consul" version="0.7.2.6" exclude="Build,Analyzers" />
		<dependency id="System.Configuration.ConfigurationManager" version="4.7.0" exclude="Build,Analyzers" />
	</group>
	<group targetFramework=".NETFramework4.7">
		<dependency id="Consul" version="0.7.2.6" exclude="Build,Analyzers" />
		<dependency id="System.Configuration.ConfigurationManager" version="4.7.0" exclude="Build,Analyzers" />
	</group>
	<group targetFramework=".NETStandard2.0">
		<dependency id="Consul" version="0.7.2.6" exclude="Build,Analyzers" />
		<dependency id="System.Configuration.ConfigurationManager" version="4.7.0" exclude="Build,Analyzers" />
	</group>
	<group targetFramework=".NETStandard2.1">
		<dependency id="Consul" version="0.7.2.6" exclude="Build,Analyzers" />
		<dependency id="System.Configuration.ConfigurationManager" version="4.7.0" exclude="Build,Analyzers" />
	</group>
</dependencies>
```

#### After
```xml
<dependencies>
	<group targetFramework="net8.0">
		<dependency id="Npgsql" version="8.0.0" exclude="Build,Analyzers" />
	</group>
</dependencies>
```

**Rationale:**
- Removed outdated frameworks (.NETCoreApp3.0, .NETFramework4.7, .NETStandard2.0, .NETStandard2.1)
- Project targets .NET 8 only
- Updated dependencies: Npgsql (PostgreSQL/QuestDB driver) instead of Consul

### 3. Output Files

#### Before
```xml
<files>
	<file src="..\README.md" target="docs\" />
	<file src="bin\Release\netcoreapp3.0\ConfigurationManager.Api.dll" target="lib\netcoreapp3.0" />
	<file src="bin\Release\netstandard2.0\ConfigurationManager.Api.dll" target="lib\netstandard2.0" />
	<file src="bin\Release\netstandard2.1\ConfigurationManager.Api.dll" target="lib\netstandard2.1" />
	<file src="bin\Release\net47\ConfigurationManager.Api.dll" target="lib\net47" />
	<file src="bin\Release\net8.0\ConfigurationManager.Api.dll" target="lib\net8.0" />
</files>
```

#### After
```xml
<files>
	<file src="..\README.md" target="docs\" />
	<file src="bin\Release\net8.0\QuestDB.Change.Tracker.Api.dll" target="lib\net8.0" />
	<file src="bin\Release\net8.0\QuestDB.Change.Tracker.Api.pdb" target="lib\net8.0" />
</files>
```

**Changes:**
- Updated DLL names to match actual assembly: `QuestDB.Change.Tracker.Api.dll`
- Added PDB file for debugging symbols
- Removed references to older framework versions
- Only includes net8.0 build output

## Summary of Updates

| Aspect | Old | New |
|--------|-----|-----|
| **Package ID** | ConfigurationManager.Api | QuestDB.Change.Tracker.Api |
| **Version** | 3.0.3.19 | 1.0.0.0 |
| **Project URL** | github.com/.../ConfigurationManager.Api | github.com/.../QuestDB.Change.Tracker |
| **Description** | Consul Client | QuestDB Change Tracker API |
| **Tags** | Consul | QuestDB,WAL,ChangeTracking,Events,Database |
| **Target Framework** | Multiple (.NET 3.0, 4.7, Standard 2.0+) | .NET 8.0 only |
| **Dependencies** | Consul, ConfigurationManager | Npgsql |
| **Output Files** | Multiple frameworks | net8.0 only (DLL + PDB) |

## Notes

- Both `pack.nuspec` and `consul-pack.nuspec` now have identical metadata
- The version `1.0.0.0` should be updated during the release process
- Ensure `bin\Release\net8.0\QuestDB.Change.Tracker.Api.dll` and `.pdb` files are built before packaging
- The NuGet package will now correctly depend on Npgsql for database connectivity

## Publishing

When ready to publish to NuGet:

```powershell
# Build the project
dotnet build -c Release

# Pack the NuGet package
nuget pack pack.nuspec

# Push to NuGet (requires API key)
nuget push QuestDB.Change.Tracker.Api.1.0.0.0.nupkg -Source https://api.nuget.org/v3/index.json
```
