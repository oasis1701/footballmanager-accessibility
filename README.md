# FM26 Accessibility Mod

A BepInEx plugin that makes Football Manager 26 accessible to blind users via the NVDA screen reader.

## Features

- **Focus-based navigation announcements** - Piggybacks on the game's native keyboard navigation to announce focused elements
- **Table row announcements** - Reads full row data when navigating tables
- **Table header announcements** - Announces all column names for context
- **Element state announcements** - Reports checked/unchecked, selected states, etc.
- **Rich Text stripping** - Cleans up markup for cleaner announcements
- **Debug tools** - Deep UI scanning and logging for troubleshooting

## Requirements

- Football Manager 26 (Unity IL2CPP)
- [BepInEx 6.x for Unity IL2CPP](https://github.com/BepInEx/BepInEx)
- .NET 6.0 SDK (for building)
- NVDA screen reader
- Tolk library (auto-downloaded during build)

## Installation

1. Install BepInEx 6.x for Unity IL2CPP in your Football Manager 26 game folder
2. Download the latest release or build from source
3. Copy `FM26Access.dll` to `<game folder>/BepInEx/plugins/FM26Access/`
4. Launch the game

## Keyboard Shortcuts

### Game Navigation
| Key | Action |
|-----|--------|
| Arrow Keys | Navigate between elements |
| Enter / Space | Activate current element |
| Tab | Focus next element |

### Mod Shortcuts
| Key | Action |
|-----|--------|
| Ctrl+Shift+D | Toggle debug mode (logs focus info) |
| Ctrl+Shift+S | Deep scan UI (writes UIDiscovery.txt) |
| Ctrl+Shift+W | "Where am I?" - Announce current focus |
| Ctrl+Shift+H | Help |
| Escape | Stop speech |

## Building from Source

### Prerequisites
- .NET 6.0 SDK
- Football Manager 26 installed

### Build

```powershell
cd D:\fm26access
dotnet build FM26Access.sln -c Debug
```

Or use the build script:

```powershell
.\build.ps1
```

The build automatically copies the plugin to the game's BepInEx plugins folder.

## How It Works

The mod monitors `FMNavigationManager.CurrentFocus` to detect when the player navigates to a new UI element. When focus changes, it extracts relevant information (label, type, state) and sends it to NVDA for announcement.

This approach leverages the game's existing keyboard navigation rather than implementing custom navigation, ensuring compatibility with the game's UI system.

## Contributing

Note: The `decompiled/` folder containing reference game code is not included in this repository. You'll need to decompile the game's DLLs yourself using ILSpy or similar tools if you need to reference the game's internal structure.

## Acknowledgments

- [BepInEx](https://github.com/BepInEx/BepInEx) - Unity modding framework
- [Tolk](https://github.com/dkager/tolk) - Screen reader abstraction library
