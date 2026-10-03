# Deathrest Unbound: dev commands. Wraps the PowerShell scripts in tools/. Run `make help` for the list.

PS := powershell -NoProfile -ExecutionPolicy Bypass -File

SCENARIOS ?= all

.PHONY: help build test package deploy play play-novre smoke smoke-novre clean unlink

help:
	@echo "Targets:"
	@echo "  make build       - dotnet build Source -c Release"
	@echo "  make test        - unit tests (power math, link resolution)"
	@echo "  make package     - build, then assemble dist/DeathrestUnbound"
	@echo "  make deploy      - build + package + junction the mod and dev harness into RimWorld/Mods"
	@echo "  make play        - deploy, then launch an isolated dev profile; a NEW colony gets the deathrest playground"
	@echo "  make play-novre  - same, without Vanilla Races Expanded - Sanguophage"
	@echo "  make smoke       - full automated in-game test run (needs Steam running, RimWorld closed)"
	@echo "                     pick scenarios: make smoke SCENARIOS=uncap,shared"
	@echo "  make smoke-novre - the in-game run without VRE Sanguophage (vanilla-only check)"
	@echo "  make unlink      - remove the Mods-folder junctions this project created"
	@echo "  make clean       - remove build output, dist/ and the smoke-test profile"

build:
	dotnet build Source -c Release

test:
	dotnet test Source/DeathrestUnbound.Core.Tests -c Release

package: build
	$(PS) tools/package.ps1

deploy:
	$(PS) tools/deploy-dev.ps1

play:
	$(PS) tools/play.ps1

play-novre:
	$(PS) tools/play.ps1 -NoVre

smoke:
	$(PS) tools/smoke-test.ps1 -Scenarios $(SCENARIOS)

smoke-novre:
	$(PS) tools/smoke-test.ps1 -Scenarios $(SCENARIOS) -NoVre

unlink:
	powershell -NoProfile -Command "foreach ($$n in 'DeathrestUnbound','DeathrestUnboundDevHarness') { $$p = Join-Path 'C:\Program Files (x86)\Steam\steamapps\common\RimWorld\Mods' $$n; if (Test-Path $$p) { cmd /c rmdir $$p; Write-Output \"Removed $$p\" } }"

clean:
	powershell -NoProfile -Command "Remove-Item -Recurse -Force -ErrorAction SilentlyContinue dist, 1.6/Assemblies, tools/DevHarness/1.6/Assemblies, tools/.smoke-profile, Source/*/bin, Source/*/obj"
