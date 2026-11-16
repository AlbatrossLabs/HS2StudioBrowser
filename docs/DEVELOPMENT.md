# Studio Browser: development

How the plugins work inside, how to build them, and where everything is in the source. The user guide is [GUIDE.md](GUIDE.md).

## How it works

- **Cards**: scanning runs on a background thread and reads only each card's header and name, never the whole card. Names are cached by path, date and size.
  - Outfit card: PNG, product number, `【AIS_Clothes】`, version, language, **outfit name**, the clothes data, then plugin data (ExtendedSave appends it). Rename splices the new name in where the old one was.
  - Character card: PNG, product number, `【AIS_Chara】`, version, language, user/data IDs, a MessagePack block header (`lstInfo`: name, version, pos, size), the data length, then the blocks (Custom, Coordinate, **Parameter**, GameInfo, Status, Parameter2, GameInfo2, and **KKEx** for plugin data). The name is `fullname` in the Parameter block. Rename rewrites that one value with a small MessagePack editor (`MsgPackLite`), then shifts the stored positions of the blocks that come after it. The blocks aren't stored in header order (KKEx is listed first but stored last), so positions are compared, not list order.
  - Scene: PNG (320 × 180), version string (`1.1.1`), the scene (objects, then every character stored exactly like a character card minus the picture, map, lighting...), then ExtendedSave's plugin data at the very end (`KKEx`, version, length). No name: the file name is used.
  - Every rename writes a new copy next to the file, reads it back (new name present, every other block byte-for-byte unchanged), then swaps it in with `File.Replace`. Tested on real cards: renaming back gives the original file byte for byte, including a 3 MB character with 3 MB of plugin data.
- **Thumbnails** load only for cards on screen, on a background thread, a few per frame into textures (pictures wider than 512 px are scaled down as they load); least recently shown are freed past *Thumbnails kept in memory*, all of them when the window closes.
- **Missing zipmods**: Sideloader saves in every card the zipmod GUID, slot and category of each mod item it uses (`com.bepis.sideloader.universalautoresolver` in the plugin data). The browser reads that list from the file and checks each entry against the zipmods Sideloader has loaded, the same test Sideloader runs when the card loads, including its rule that an item whose saved ID belongs to a base-game item doesn't count. Background thread, results kept for the session.
  - Scenes: the scene's own list (`itemInfo`, checked against Sideloader's studio-item table by GUID and slot; entries Sideloader never remaps, such as bone lists, only need the zipmod present), the map (`mapInfoGUID`), and each embedded character's list, found by scanning the scene for the character tag (`【AIS_Chara】`) and reading that character's blocks. Sideloader's studio lookups are brought up to date on the main thread first, so the worker only reads.
- **Studio's shortcuts** are paused by making `Studio.isInputNow` report true while the pointer is over a browser (the same check Studio uses while you type in its own fields). One hook, shared by every browser.
- **Plugin structure**: `StudioBrowserPlugin` (Core) holds the settings, hotkey, toolbar button and the open/close/stand-in logic; each plugin derives from it and supplies a kind. Every kind is a `StudioListKind` (names, and how Studio's list is found, hidden and closed). A `BrowserKind` adds cards on disk (where they live, how a name is read and written, its buttons) and gets the thumbnail window; an `ItemKind` adds a catalog of groups, categories and items read from the game's lists, and gets the list window (`ItemWindow`: favorites, recent, search, stepping). Adding a kind means writing those pieces only.

## Build

Requires the .NET SDK (6 or newer). References are read straight from the game install (`HS2Dir` in `Directory.Build.props`, set to `X:\HS2 RX15`); .NET Framework reference assemblies come from NuGet.

```
dotnet build -c Release
```

Builds all seven DLLs into `bin\Release\` and copies them to `X:\HS2 RX15\BepInEx\plugins\HS2StudioBrowser\`. Point it elsewhere with `-p:HS2Dir="D:\Other\HS2"`, or add `-p:Deploy=false` to skip the copy. Build one project with e.g. `dotnet build src/Characters -c Release` (it builds Core too).

Built against BepInEx 5.4, HS2API 1.48, Harmony 2.9, Sideloader 21.1.

## Source map

```
src/Core/                       HS2StudioBrowser.Core.dll
  StudioBrowserPlugin.cs        base plugin: settings, hotkey, toolbar, open/close/stand-in, Camera Sweep step-aside, migration
  StudioListKind.cs             what every browser supplies: names, Studio's list (find, hide, close)
  BrowserKind.cs                file browsers: cards on disk (BrowserKind, CardAction, ActionResult, ListChange)
  ItemKind.cs                   catalog browsers: groups › categories › items (ItemKind, ItemGroup, ItemCategory, CatalogItem)
  UI/ItemWindow.cs              the list window for catalog browsers: groups panel, favorites, recent, search, stepping
  UI/IBrowserWindow.cs          what the plugin needs from either window
  Files/KeyList.cs              favorites / recent by key, for catalog browsers
  Files/Tags.cs                 tag lists (TagSet, from .tagset.txt, plus the shared list), tags on cards or items (TagStore), the filter rule
  Files/AutoTags.cs             automatic tags read from cards on a worker thread, cached (AutoTagIndex)
  Files/AutoFacet.cs            helper for a kind's automatic groups
  Files/Duplicates.cs           duplicate finder: exact keys, near matches within a tolerance, on a worker thread
  UI/TagRadial.cs               the tag wheel (tagging and filtering)
  TagSets.cs                    starting tag lists for each browser
  UI/BrowserWindow.cs           the window: tree, grid, favorites, missing-mod badges, footer, menus, dialogs, drag and drop
  UI/Styles.cs Theme.cs         look (VN Deck / Albatross Labs colours, same as Camera Sweep)
  UI/ToolbarButton.cs           left-toolbar button (KKAPI)
  Files/CardIndex.cs            folder tree and card list, scanned on a worker thread
  Files/ThumbnailCache.cs       on-screen thumbnails, background reads, LRU
  Files/Favorites.cs            favorites file; follows renames and moves
  Files/FileOps.cs              rename, move, duplicate, folders, Recycle Bin, Explorer
  Files/MsgPackLite.cs          minimal MessagePack reader/writer for in-place card edits
  Files/CharaCard.cs            character card: block layout, read/rename name, plugin data (also used for characters inside scenes)
  Game/ModCheck.cs              missing-zipmod check and its background worker
  Game/InputGuard.cs            pauses Studio's shortcut keys while a browser has the pointer
  Game/CameraSweepBridge.cs     is Camera Sweep picking right now?
  Game/StudioTargets.cs         selected characters
src/Outfits/                    HS2StudioBrowser.Outfits.dll
  OutfitBrowserPlugin.cs        plugin (GUID com.albatrosslabs.hs2.studiobrowser.outfits)
  OutfitKind.cs                 outfits as a BrowserKind
  CoordinateCard.cs             outfit card: read/rename name, plugin data
  OutfitLoader.cs               Load / Clothes only / Accessories only
  CostumeList.cs                Studio's Costume list: visibility, panel character, in-memory sync (+ Browser Folders' copy)
  CostumeOverride.cs            hides the Costume list and Browser Folders' outfit window
  ToolbarIcon.cs                hanger icon
src/Scenes/                     HS2StudioBrowser.Scenes.dll
  SceneBrowserPlugin.cs         plugin (GUID com.albatrosslabs.hs2.studiobrowser.scenes); Close after loading, Save into the open folder
  SceneKind.cs                  scenes as a BrowserKind: Load / Import, Browser Folders' scene window and save folder
  SceneBytes.cs                 scene file bytes: name check, plugin data at the end, embedded characters (no game types)
  SceneFile.cs                  missing zipmods for scenes: studio items, map, embedded characters
  LoadScreen.cs                 Studio's Load screen: hide on creation, find, hide (canvases off), close, refresh
  SceneIcon.cs                  framed-landscape icon
src/Poses/                      HS2StudioBrowser.Poses.dll
  PoseBrowserPlugin.cs          plugin (GUID com.albatrosslabs.hs2.studiobrowser.poses, needs Pose Tools); Show dat poses, Save into the open folder
  PoseKind.cs                   poses as a BrowserKind: Load, Picture, Save as, .dat switch
  PoseFile.cs                   pose file bytes: .dat / .png layout, read/rename name, write with a new picture, bones for the duplicate check (no game types)
  PoseList.cs                   Studio's Pose list: find, hide (CanvasGroup), close, refresh; Pose Tools' current folder
  PoseIcon.cs                   posed-figure icon
src/Animations/                 HS2StudioBrowser.Animations.dll
  AnimationBrowserPlugin.cs     plugin (GUID com.albatrosslabs.hs2.studiobrowser.animations)
  AnimationKind.cs              animations as an ItemKind: Play
  AnimationTable.cs             Studio's animation table into the catalog; zipmod of each (Sideloader); Animation Search's full copy
  AnimationLengths.cs           length / frames / loop of each animation: background probe on a hidden Animator, from the character on play, cached
  AnimeControls.cs              the animation box in the browser: speed, pattern, options (with undo), timeline scrub, props, loop, restart, copy / paste
  AnimePanel.cs                 the Animations page: hide its three lists (and Animation Search's box), close, play (through Studio's list for Animation Pairing)
  AnimationIcon.cs              play-button icon
src/Items/                      HS2StudioBrowser.Items.dll
  ItemBrowserPlugin.cs          plugin (GUID com.albatrosslabs.hs2.studiobrowser.items)
  ItemBrowserKind.cs            items as an ItemKind: Add, Add inside
  ItemTable.cs                  Studio's item table into the catalog; zipmod of each (Sideloader); color count
  ItemThumbs.cs                 item pictures: Quick Access Box, thumbnail packs (Sideloader), own cache, off-screen render
  ItemPanel.cs                  the Item list: hide its three columns, close through the Add menu, add (inside a node)
  ItemIcon.cs                   box icon
src/Characters/                 HS2StudioBrowser.Characters.dll
  CharacterBrowserPlugin.cs     plugin (GUID com.albatrosslabs.hs2.studiobrowser.characters)
  CharacterKind.cs              characters as a BrowserKind: Load (add) / Change (swap)
  CharaLists.cs                 Studio's character lists: find, hide, close via the Add menu, in-memory sync (+ Browser Folders' copy)
  CharacterIcon.cs              head-and-shoulders icon
```
