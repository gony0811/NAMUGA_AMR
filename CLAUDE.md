# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

C# .NET 8.0 class library project (NAMUGA AMR). Nullable reference types and implicit usings are enabled.

## Build Commands

```bash
dotnet build                 # Build (Debug)
dotnet build -c Release      # Build (Release)
dotnet clean                 # Clean build artifacts
```

## Project Structure

- `AMR.sln` — Solution file
- `AMR/AMR.csproj` — Main project (net8.0 class library)
- `AMR/` — Source code directory

## Development Environment

- .NET SDK 8.0
- IDE: JetBrains Rider
