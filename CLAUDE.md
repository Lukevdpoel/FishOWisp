# FishOWisp2

A cozy fishing game built in Unity 6 (6000.2.5f1) using the Universal Render Pipeline (URP).

## Project Structure

```
Assets/
├── ART/            - Models, animations, textures, audio, particles, shaders, sprites, UI art
├── Editor/         - Custom editor tools
├── Materials/      - Material definitions
├── Plugins/        - Sirenix Odin Inspector
├── Prefabs/        - PlayerMaster, GameplayUI, Managers, FishPhotoStudio, FlockManager
├── Resources/      - Runtime-loaded assets
├── Scenes/         - MAIN, HUTBUILT, CAVE, AquariumRoom, Test
├── ScriptableObjects/
│   ├── Fishes/     - FishPreset assets (Carp, CoyFish, Electric Eel, Lamprey, Pike, PufferFish, Stiphodon)
│   └── Pools/      - FishPool data
├── Scripts/        - All C# game code (~70 scripts)
├── Settings/       - URP renderer and pipeline settings
├── Shaders/        - Custom shaders
└── Textures/       - Texture assets
```

## Key Scripts by System

### Player (`Scripts/PlayerController/`)
- **PlayerController.cs** - Movement, orbit camera, interaction handling, animation states

### Fishing (`Scripts/Fishing/`)
- **FishingRodController.cs** - Main fishing state machine (Idle → Charging → WaitingForBite → FishOnTheLine → FightingFish → Reeling → InspectingCatch → Cooldown)
- **FishingEvents.cs** - Static event hub for all fishing actions
- **RodCasting.cs** - Charge-based casting mechanic
- **BobberController.cs** - Bobber physics
- **FishingLine.cs** / **VerletRope.cs** - Line physics (Verlet integration)
- **FishPool.cs** - Fish spawning and management
- **FishFightHandler.cs** - Fish fight ("play as the fish"): tank-steer the hooked fish, reel it to the waterbank; the fish sporadically fights back by turning away. (DirectionalFishingMinigame.cs is the retired old fight, no longer referenced.)

### Encyclopedia & Inventory (`Scripts/EncyclopediaManagment/`)
- **FishPreset.cs** - ScriptableObject defining fish species (rarity, size, price, bait/weather preferences)
- **FishEncyclopediaManager.cs** - Singleton managing caught fish registry, save/load to JSON
- **CaughtFish.cs** - Serializable fish instance
- **PlayerInventory.cs** - Singleton inventory (24 slots, currency system)
- **InventoryUI.cs** / **InventorySlotUI.cs** - Grid-based inventory UI

### Bounty Board (`Scripts/BountyBoard/`)
- **BountyBoard.cs** - Daily bounty generation (5/day), delivery tracking, 1.5x reward multiplier

### NPC & Dialogue (`Scripts/NPC/`)
- **DialogueManager.cs** - Singleton dialogue UI with typewriter effect and choice branching
- **DialogueNPC.cs** / **DialogueZone.cs** - NPC interaction triggers

### Vendor (`Scripts/HutInside/Vendor/`)
- **FishVendor.cs** - Sell caught fish for currency
- **FishTankManager.cs** / **FishTankDropZone.cs** - Aquarium display

### Lighting (`Scripts/Lighting/`)
- **AdvancedDayNightCycle.cs** - Real-time day/night cycle with LUT crossfading

### Effects (`Scripts/Effects/`)
- **FishBoid.cs** / **FlockManager.cs** - Fish schooling AI

## Architecture Patterns

- **Singletons** for managers: PlayerInventory, FishEncyclopediaManager, DialogueManager (via GenericSingleton<T>)
- **Event-driven** decoupling: FishingEvents static event hub, DialogueManager.OnDialogueStateChange, BountyBoard.OnBountyBoardStateChange
- **ScriptableObjects** for fish data configuration
- **Data flow**: Fish caught → CaughtFish → PlayerInventory.AddFish() → FishEncyclopediaManager.RegisterCaughtFish()

## Key Enums

- `Rarity` - Common, Uncommon, Rare, Epic, Legendary
- `BaitType` - Worm, Insect, Minnow, Bread, Synthetic
- `FishingState` - Idle, Charging, WaitingForBite, FishOnTheLine, FightingFish, Reeling, InspectingCatch, Cooldown

Fish spawning is gated by time of day (not weather): each `FishPreset` has separate
`daySpawnChance` / `nightSpawnChance` weights, and `WorldStateManager.IsNight` decides
which applies (natural clock in Auto mode, or forced by the vendor's Night Lantern).

## Persistence

- **Encyclopedia**: JSON at `Application.persistentDataPath/encyclopedia.json`
- **Bounties**: PlayerPrefs (daily reset)
- **Currency**: In-memory via PlayerInventory.currentCurrency

## Dependencies

- Unity 6 (6000.2.5f1)
- URP v17.2.0
- Input System v1.14.2
- Sirenix Odin Inspector (editor tooling)
- TextMesh Pro (UI text)
- Polybrush / Terrain Tools (level design)

## Build & Run

Open the project in Unity 6. Main gameplay scene is `Assets/Scenes/MAIN.unity`.

## Conventions

- C# scripts use PascalCase for classes and methods
- Events use `On` prefix (e.g., `OnFishBite`, `OnStartReeling`)
- Manager classes follow singleton pattern via `GenericSingleton<T>`
- Fish species are defined as ScriptableObject assets in `ScriptableObjects/Fishes/`
- Note: the folder is spelled `EncyclopediaManagment` (not "Management")
