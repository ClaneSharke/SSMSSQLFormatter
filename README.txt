SSMSSQLFormatter - CI build fix  (target release: v2.3.4)
=========================================================

WHAT THIS BUNDLE IS
-------------------
The two corrected project files, in their correct folders. Extract this zip
at the repository root:

    F:\GitHub\SSMSSQLFormatter

so that these two files OVERWRITE the existing ones:

    tools\FormatterBench\FormatterBench.csproj
    src\SsmsSqlFormatter.Tests\SsmsSqlFormatter.Tests.csproj


WHAT WAS BROKEN
---------------
FormatterBench.csproj and the Tests .csproj are old-style (non-SDK) projects,
which do NOT auto-include source files. Program.cs and ScriptDomFormatterTests.cs
existed on disk but were never listed, so the compiler got zero sources.
FormatterBench is an .exe, so "no Main method" -> error CS5001 -> the whole
solution build failed BEFORE the release/upload step ever ran.

Each csproj now (1) lists its .cs file via <Compile Include> and (2) references
System / System.Core, matching the pattern the main project already uses.


STEPS AFTER EXTRACTING  (run one line at a time, from F:\GitHub\SSMSSQLFormatter)
--------------------------------------------------------------------------------

1) Remove the misplaced copy left by the earlier attempt:

   del src\SsmsSqlFormatter\FormatterBench.csproj

2) Confirm the real FormatterBench now lists its source (this should PRINT a line):

   findstr /C:"Compile Include" tools\FormatterBench\FormatterBench.csproj

3) Bump the version (updates the VSIX manifest + AssemblyInfo to 2.3.4):

   powershell -ExecutionPolicy Bypass -File tools\update-version.ps1 -Version 2.3.4

4) Stage and commit:

   git add tools/FormatterBench/FormatterBench.csproj src/SsmsSqlFormatter.Tests/SsmsSqlFormatter.Tests.csproj src/SsmsSqlFormatter/source.extension.vsixmanifest src/SsmsSqlFormatter/Properties/AssemblyInfo.cs

   git commit -m "Fix CI build (CS5001) and bump to v2.3.4"

5) Re-point the v2.3.4 tag to this commit and push:

   git tag -f v2.3.4
   git push
   git push -f origin v2.3.4

6) Watch the build:

   gh run watch

If step 2 prints nothing, the FormatterBench file did not land correctly -
say so and I'll re-send it.
