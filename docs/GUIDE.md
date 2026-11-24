# Studio Browser: full guide

Everything each browser does, in detail. For a quick overview and install steps, see the [README](../README.md); for how it works inside and how to build it, see [DEVELOPMENT.md](DEVELOPMENT.md).

## Install details

Put `HS2StudioBrowser.Core.dll` and the browser DLLs you want in `BepInEx\plugins` (any subfolder). The core is required; each browser works on its own with it.

| Needs | For |
|---|---|
| BepInEx 5.4, HS2API (KKAPI for HS2) | everything |
| Pose Tools (`HS2_PoseTools.dll`) | the Poses browser (it reads and saves the `.png` poses) |
| Sideloader | the missing-zipmod check, item and animation sources, the deletion report |

Used when installed, never required: **Quick Access Box** (item pictures), **Accessories to Studio Items** (its items and icons), **Animation Search** and **Animation Pairing** (Animations), **Browser Folders** (kept in step, its windows hidden while a browser stands in), **Camera Sweep** (the browser steps aside while it picks).

**Coming from Outfit Browser 0.4**: remove `BepInEx\plugins\HS2OutfitBrowser\`. On first start the Outfits browser copies your settings (`com.albatrosslabs.hs2.outfitbrowser.cfg`) and favorites (`HS2OutfitBrowser.favorites.txt`) over.

## Every browser

- **Open it**: open Studio's own list (Costume, Add › Character › Female / Male, Add › Item, System › Load, Kinematics › Pose, or Animations). The browser takes its place: Studio's list (and Browser Folders' folder window for it) stays hidden, and closing the browser (**x**, its toolbar icon or its hotkey) closes Studio's list too. The toolbar icon (a hanger for outfits, a head and shoulders for characters, a landscape for scenes, a posed figure for poses, a play button for animations, a box for items) or the hotkey also opens it at any time.
  - F1 › *Replace the Costume list* / *Replace the Character list* off: Studio's list shows next to the browser again (and *Open with the ...* decides whether the browser opens with it).
  - While **Camera Sweep** is picking cards (*+ Pick in Studio's list*), the browser steps aside and Studio's list shows as normal, since Camera Sweep picks with Studio's own buttons. The browser comes back when you press *Done*.
- **Female / Male** at the top picks the female or male folder (outfits and characters; scenes and poses aren't split). It follows the list Studio opened (and, for outfits, the character in the Manipulate panel).
- **Narrow windows**: the top bar, the tag filter, the footer and the animation controls wrap onto extra rows instead of cutting buttons off.
- **Folder tree** (left): click a folder to show its cards. The chevron opens or closes a folder's subfolders (double-click the name does the same); right-click › *Expand all* / *Collapse all* does a whole branch. Open folders are remembered. The number is how many cards are in that folder, subfolders included. A folder whose cards are all in its subfolders shows them when opened, as if *Subfolders* were ticked. The double chevron at the top folds the panel away for more thumbnail room.
- **Thumbnails** (right): the name stored in the card (what Studio's list shows), with the file name under it when the two differ. Search matches either. Sort by *Name*, *File*, *Newest* or *Oldest*. The slider sets the picture size. Tick *Subfolders* to also show cards in the folders inside the open one.
- **Favorites**: right-click › *Add to favorites*, or click the star in a thumbnail's top-left corner (it shows on hover). Favorites sit at the top of every folder under a *Favorites* heading, the rest below, each part in the chosen order.
- **Favorites in the tree**: **★ Favorites** at the top of the folder tree shows every starred card, from this folder and every folder inside it. Your own folders sit under it, to sort favorites further:
  - **New folder**: **+** on the Favorites row, or *+ new folder* in the details panel (which also puts the selected cards in it).
  - **Put cards in**: click the folder's chip in the details panel, or drag cards onto the folder (or onto Favorites itself to just star them). A card put in a folder becomes a favorite; it can be in several folders.
  - **Open, rename, delete**: click a folder to see its cards; right-click it to rename or delete. Deleting a folder leaves its cards favorites.
  - The folder list is kept in `BepInEx\config\HS2StudioBrowser.<kind>.folders.txt`; which cards are in which folder is kept with the tags (`Folder:<name>`), so it follows renames and moves made in the browser.
- **Missing zipmods**: cards that use zipmods you don't have get a red **!** badge; hovering one names the missing mods in the footer. Tick **Missing mods** at the top to show only those. Right-click › **Missing mods...** lists each missing zipmod with its author, GUID and website, which part uses it, and whether it's *not installed* or *installed, outdated* (you have the mod, but your version lacks that item). With several cards selected it adds up which mods are needed and by how many cards. **Copy list** puts it on the clipboard. Right-click on empty space › *Select ... with missing mods*, then drag them onto a folder to sort them out in one go.
- **Arrow keys** move the selection through the grid (Up / Down by row, across Favorites and the rest; Page Up / Down, Home / End). In the Poses browser they also try each pose on.
- **Select**: click. **Ctrl+click** adds or removes one, **Shift+click** selects a range, **Ctrl+A** selects all, click empty space to clear.
- **Rename** (right-click or F2) has two boxes: the **name** stored inside the card, and the **file name**. *File name follows the ... name* (on by default, F1 › *Rename the file with the ...*) fills the file name in as you type. Only the name is rewritten: the picture, the rest of the card and every plugin's data stay exactly the same, and the file keeps its date.
- **Move**: drag cards (or a folder) onto a folder in the tree, or right-click › *Move to...* (with a box to make a new folder). A name that's taken in the target folder gets ` (2)`.
- **Recycle Bin**: nothing is ever deleted outright; cards and folders go to the Windows Recycle Bin after a confirmation.
- **Keys**: while the pointer is over a browser (or one of its menus or boxes is open), Studio's own shortcuts are paused, so **Enter** (main action), **F2** (rename), **Del** (Recycle Bin), **Esc**, **Ctrl+A** and **Ctrl+F** (search) only act on the browser. Move the pointer off and Studio's keys work as usual.

### Outfits

| Button | Does |
|---|---|
| **Load** (double-click, Enter) | The whole outfit, exactly as Studio's Costume list *Load* does it (plugin data saved with the outfit comes along) |
| **Clothes only** | Keeps the character's accessories, takes the clothes from the outfit |
| **Accessories only** | Keeps the character's clothes, takes the accessories |

The outfit goes on every character selected in the workspace (or the one in the Manipulate panel if none is selected). Characters of the other sex are skipped. The footer says who it will load onto.

### Characters

| Button | Does |
|---|---|
| **Load** (double-click, Enter) | Adds the character to the scene (Studio's *Load*) |
| **Change** | Swaps the selected character(s) of that sex in the workspace for this one, keeping their place and pose (Studio's *Change*) |

The name in Rename is the character's name (*fullname*), which is what Studio's character list shows.

### Scenes

| Button | Does |
|---|---|
| **Load** (double-click, Enter) | Replaces the current scene with this one (Studio's *Load*) |
| **Import** | Adds this scene's characters and objects to the current scene (Studio's *Import*) |

- Thumbnails are Studio's 320 × 180 scene pictures; the slider goes up to 420 px wide. Sorted newest first by default, like Studio's Load screen. Unlike Studio's Load screen, every subfolder is browsable (Studio's own screen only lists `studio\scene` itself; Browser Folders adds folders to it).
- Scenes store no name of their own, so the card shows the file name and **Rename** changes only the file name.
- **Close after loading** (F1, on by default): after Load the browser and Studio's Load screen close so you see the scene. Off: stay open to load another. Import always stays open.
- **Saving** (System › Save): Studio always saves a new file named by the date (`2026_1122_1333_05_123.png`) into `studio\scene`. The browser changes two things, for Studio's Save button only (autosave and other plugins keep their own paths):
  - **Where it goes** (F1 › *Save new scenes into*): **LoadedScene** (default) saves into the folder of the scene you loaded, so a scene opened from `studio\scene\Projects\Beach` saves next to it; with a new scene (System › Init), into the folder open in the browser. **OpenFolder** always uses the folder open in the browser. **Studio** leaves it to Studio (or Browser Folders' *Save scenes to current folder*). Only folders inside `studio\scene` are used.
  - **Its name** (F1 › *Name scenes when saving*, on by default): right after the save a small box asks for a file name, filled in with the loaded scene's name. **Enter** names it; **Esc** or *Keep date name* leaves Studio's name. A name that's taken gets ` (2)`; the box says so before you press Enter. Saving never replaces a scene.
- **Save into the open folder** (F1, on by default): with Browser Folders' *Save scenes to current folder* on, Browser Folders' current scene folder follows the one open in the browser.
- **Missing zipmods** for scenes covers the scene's studio items and lights, its map, and every character in the scene (each carries its own list of clothes and accessories). The Missing mods list names the part: *studio item*, *map*, or *Aiko: top*.
- Studio's own Load screen deletes scenes outright; here they go to the Recycle Bin.

### Poses

| Button | Does |
|---|---|
| **Load** (double-click, Enter) | Puts the pose on the characters selected in the workspace (or, with none selected, the one whose Pose list is open) |
| **Picture** | Gives the pose a picture of what Studio's camera sees: puts it on the selected character, waits for the bones to settle, takes a 16:9 shot (640 × 360 by default, rendered at twice that and scaled down for smooth edges; F1 › *Picture size* goes from Studio's 320 × 180 up to 1280 × 720, and *Smooth edges* turns the 2× off). A `.dat` becomes a Pose Tools `.png` with the same pose data (the `.dat` goes to the Recycle Bin); a `.png` gets its picture replaced. Right after Load it shoots the pose as it stands, so frame the camera or tweak bones first. |
| **Arrow keys** (Page Up / Down, Home / End) | Move through the grid as it's laid out, and put each pose on the selected characters as you land on it (*Try on* in the top bar, on by default; F1 › *Try poses with the arrow keys*). Off: the arrows only move the selection. |
| **Undo** (Backspace) | Puts the characters back in the pose they had before you started trying poses in the browser. Closing the browser keeps the pose you ended on. |
| **Save as** … **Save** (footer) | Saves the selected character's pose into the open folder, through Pose Tools (a `.png` with a picture of the view) |

- **No picture** (top bar): shows only the poses without a picture (the `.dat` ones) from the open folder and every folder in it; the folder tree's counts then say how many each folder still has. Select one (or step with the arrow keys, which try each pose on), frame the camera, press **Picture**: the pose gets its picture, drops out of the list, and the next one is selected. Untick to see everything again.
- Two formats, same pose data: Studio's own `.dat` (no picture) and Pose Tools' `.png` (a picture with the `.dat` bytes after it, so Studio needs Pose Tools to read them). The **.dat** switch in the top bar (F1 › *Show dat poses*, on by default) shows `.dat` poses too, as cards without a picture.
- Rename changes the pose's stored name (what Studio's Pose list shows) and, if ticked, the file name; everything after the name is kept byte for byte, `.dat` and `.png` alike.
- Poses aren't split by sex (Pose Tools lists all of them for either sex), so there's no Female / Male switch. There's nothing for the missing-zipmod check to read in a pose, so it's off for poses.
- **Save into the open folder** (F1, on by default): Pose Tools' current folder follows the folder open in the browser, so Studio's Pose list and new saves (from the browser or from Studio's list) use it.
- Load uses Studio's own pose loading with Pose Tools and ExtendedSave hooked in, so Pose Tools' *Load Expression* and *Load Skirt FK* settings apply as usual.

### Animations

Not files: Studio's animations come from the game's and your zipmods' lists, as **group › category › animation**, with no pictures. So this browser is a searchable list: no rename, move or Recycle Bin.

| | Does |
|---|---|
| **Click** a row (or **Play**, double-click, Enter) | Plays it on the characters selected in the workspace (or, with none selected, the one in the Manipulate panel). *Play on click* off (top bar, or F1): only double-click / Enter / Play plays. |
| **Up / Down**, Page Up / Down, Home / End | Steps through the list, playing each one as you go (with *Play on click*) |
| **Star** (or **F**) | Adds or removes a favorite |
| **Right-click** (or **T**) | Opens the tag wheel for it (see *Tags*) |
| **Search** | Every animation at once: name, group, category or zipmod. Down or Enter jumps to the first match. |

- The left panel has **Favorites** and **Recently used** (the last 40 played) at the top, then every group (click it for all its animations; the chevron opens its categories) and category, in Studio's order. Sort by *Studio* order or *Name*; favorites always come first.
- Each row shows where the animation comes from: the zipmod's name and author, or *base game*. In search, Favorites and Recent it also shows *group › category*.
- With two or more characters selected and **Animation Pairing** installed, Play goes through Studio's own list, so Pairing gives each character its partner animation as usual. Without Pairing, everyone gets the same animation.
- **Animation controls** (the strip under the list; the chevron folds it): everything in Studio's animation box, for the selected characters.
  - **Speed** (0–3, *1×* resets it), **Pattern**, and **Option 1 / 2** (H animations only), with the same undo steps as Studio's sliders (one per drag, so Ctrl+Z works as usual). Speed also applies to selected animated items, as in Studio.
  - **Time**: where the animation is now, as *frame 12 / 72*; drag it to jump to any frame on every selected character. At speed 0 the character stays on that frame, so any frame of an animation can become a pose (and is saved with the scene).
  - **Off / FK / IK** (right end of the strip's header, so they're there even when the strip is folded): **Off** turns FK and IK off so the animation moves the character again (a character with FK or IK on holds its pose and ignores the animation); **FK** and **IK** turn forward or inverse kinematics on or off, as Studio's Kinematics › FK / IK switches do. Studio keeps FK and IK exclusive, so turning one on turns the other off. Each applies to all selected characters. Lit means it's true for every one of them; neon text means only some (a click makes it all). Studio's Kinematics panel follows.
  - **Props** (the animation's own items), **Loop** (keep animations that play once looping), **Restart**, **Restart all** (every animation in the scene, in step), **Copy** / **Paste** (one character's animation onto others).
  - While the browser stands in for the Animations page, Studio's own box is hidden with its lists and kept in step with what you change here. F1 › *Show animation controls* off brings Studio's box back instead.
- Only Studio's three list columns (and, with the controls on, its animation box) are hidden while the browser stands in for them. **Animation Search**'s box is hidden too; the browser reads (and plays from) the full list it keeps, so its filter doesn't hide anything here.
- Right-click (or T) opens the tag wheel for the animation; the star (or F) is for favorites. The **Tags** button filters across every animation. The animation tag list starts with Object, Position, Sex position, Gender, Type, Energy and Use.
- **Lengths**: each row shows how long the animation runs (`2.4 s · 72 f`), *once* for one that doesn't loop, and **pose** for a 1–2 frame animation (a held pose rather than a motion). The first time the browser opens it measures every animation in the background, controller by controller on a hidden Animator, a few milliseconds a frame (the footer shows progress), and remembers the results in `BepInEx\config\HS2StudioBrowser.animations.lengths.txt`; an animation you play is measured again from the character. F1 › *Measure animation lengths* off skips the background pass. Delete the file to measure everything again.
  - **pose** means 1-2 frames, or a clip of half a second or less that doesn't loop (Illusion often stores a held pose as a short still clip): about 160 of 8,500 here, against 12 by frame count alone.
  - The filter wheel adds groups of its own: **Length** (Pose, Under 2 s, 2-10 s, Over 10 s), **Playback** (Loop, Once) and **Source** (Base game, Mod). Filter by *Length: Pose* to list every pose-like animation. Search matches them too ("pose").
  - H animations with speed or pattern blends report their longest clip.
- Favorites are stored by the zipmod's GUID and its own animation number (Sideloader renumbers mod animations at every start), so they survive adding or removing other mods: `BepInEx\config\HS2StudioBrowser.animations.favorites.txt` and `.recent.txt`.

### Items

The same list as Animations (groups and categories on the left with Favorites and Recently used, search across everything, stars, tags), for Studio's items.

| Button | Does |
|---|---|
| **Add** (double-click, Enter) | Adds the item to the scene, exactly as Studio's Item list does (at Studio's start position; selected and the Add menu closed if Studio's options say so) |
| **Add inside** | Adds it inside the folder or object selected in the workspace, as if dragged onto it (one node selected) |

- Clicking an item only selects it (Studio's list adds on click): adding puts something in the scene, so the browser waits for a double-click, Enter or Add. Tick **Add on click** in the top bar (or F1) to add on click like Studio. The arrow keys never add, they only move through the list.
- **Pictures**: items show as a grid of pictures (**Grid** in the top bar, with a size slider; untick for the list of names). For each item on screen the browser looks, in order:
  1. **Quick Access Box** (if installed): every picture it can show, so the zipmod thumbnail packs it reads and the icons other plugins give it (Accessories to Studio Items hands it each accessory's own icon).
  2. **Thumbnail packs** directly, through Sideloader, by Quick Access Box's item id (`GGGGGGGG-CCCCCCCC-SSSSSSSS-HASH`, its older name-based id, and the short one), for when Quick Access Box isn't installed or hasn't loaded yet.
  3. **Pictures it made before**, in `BepInEx\cache\HS2StudioBrowser.items.thumbs`.
  4. Only then it **makes one**: loads the item off-screen on a layer of its own, frames it and shoots it from the front-left and a little above (as Quick Access Box's generator does), 128 px, saves it to that folder, and unloads it. One per frame, only for items on screen, so a new category fills in over a few seconds and is instant after that. An item with nothing visible in it (a sound, an empty helper) shows *no picture*.
  The footer says where the pictures on screen came from. Pictures are freed when the window closes.
- Each row shows the zipmod (name and author) or *base game*, and how many colors the item takes (Studio shows that as the name's color: red 1, cyan 2, green 3, yellow 4).
- Items from Accessories to Studio Items show up in its own group like any other.
- **Stay open after adding** (F1, on by default; Items and Characters): adding from the browser keeps Studio's Add menu, and so the browser, open even when Studio's option to close the menu after adding is on. Off: Studio's option decides, as in its own lists. Adding from Studio's own lists is unchanged.
- Favorites, tags and Recently used (the last 40 added) are stored by the zipmod's GUID and its own item number, so they survive adding or removing other mods.

### Maps

The same list as Items (Favorites, Recently used, search, stars, tags, the details panel, rename and mark for deletion), for Studio's maps. Studio's own Map list has no groups, so the left panel groups maps by where they come from: **Base game**, then **Mods** with one entry per mod author.

| Button | Does |
|---|---|
| **Load** (double-click, Enter) | Loads the map into the scene, replacing the current one, as Studio's Map list does |
| **Picture** | Gives the map that's loaded now a picture of exactly what's on screen (menus and characters left out). Frame the camera first; it replaces the map's picture |
| **No map** | Clears the scene's map (Studio's list's top entry) |

- **Pictures**: load a map, frame the view you like and press **Picture**. For one frame Studio's menus, the browser windows, the move gizmos and the characters (F1 › *Leave characters out*) are hidden, the screen is read exactly as it looks (your graphics plugin's lighting and exposure included), and everything comes back. The picture is cropped to 16:9 from the middle and saved at 640 × 360. Pictures are kept in `BepInEx\cache\HS2StudioBrowser.maps.pictures`, by the zipmod's GUID and its own map number, so they survive adding or removing other mods. Press Picture again any time to replace one. *Picture: No picture* in the filter wheel lists the maps still to do.
- **Map controls** (the strip under the grid): the loaded map's name, its **Options** switch (the props a map can show or hide) and **Lights** switch (its own lamps), each greyed out when the map has none, and **Reset position** when the map was moved or turned in Studio's map settings. Studio's own map panel is kept in step.
- **Automatic tags**: **Source** (Base game, Mod), **Extras** (Options, Lights, Water, None; learned when a map loads, kept in `BepInEx\cache\HS2StudioBrowser.maps.info.txt`) and **Picture** (Has picture, No picture). Filter by *Picture: No picture* to see which maps still need one.
- **Your tags** start with Place, Setting, Mood, Size and Time of day, plus the shared Project, Rating and Use groups.
- **Load** waits for a double-click, Enter or the button (a map takes a few seconds to load), and the arrow keys only move through the list. *Stay open after adding* (F1) keeps Studio's Add menu and the browser open after a load.
- **Deletion report**: a map zipmod can be moved to `mods_to_delete` when all its maps are marked and it holds nothing else.

### Favorites folders (Items, Animations, Maps)

The list browsers get the same favorites folders as the card browsers: **★ Favorites** in the left panel, your folders inside it (the chevron folds them), and *+ new folder* at the end.

- **Put things in**: click the folder's chip in the details panel, or drag an item from the list or grid onto the folder (or onto Favorites to just star it). Anything put in a folder becomes a favorite; an item can be in several folders.
- **Rename or delete**: right-click the folder. Deleting a folder leaves its items favorites.
- Folders made before 0.20 (when they had their own section) move under Favorites, and what's in them is starred.
- Kept in `BepInEx\config\HS2StudioBrowser.<kind>.folders.txt`; membership is stored with the tags (`Folder:<name>`), by the same ids as favorites.

### Rename and mark for deletion (Items, Animations)

| | Does |
|---|---|
| **F2** (or **Rename** in the footer) | Gives the item or animation your own name. Enter saves, Esc cancels, *Original name* (or an empty name) goes back. The footer shows the original. Search finds it by either name. |
| **Del** (or **Mark** / **Unmark**) | Marks it for deletion: it leaves the list (the next one is selected, so Del can go down a list) and shows under **Marked for deletion** at the top of the left panel, in red. Del there unmarks it. |
| **Deletion report** (footer, in *Marked for deletion*) | Groups the marked ones by zipmod: how many of each mod's items are marked, its file, and whether it can go. |

- **Names** are kept in `BepInEx\config\HS2StudioBrowser.<kind>.names.txt` (by the same ids as favorites) and, with F1 › *Use my names in Studio* (on), also show in Studio's own Item / Animations lists while the game runs. Only the running game's copy changes: the zipmod is never edited, and scenes don't store item names, so nothing breaks if you remove the file.
- **Marked** ones are kept in `BepInEx\config\HS2StudioBrowser.<kind>.marked.txt`. F1 › *Hide marked for deletion* off shows them everywhere, in red, instead of only under Marked for deletion.
- A zipmod **can be removed** only when every item (or animation) in it is marked and it holds nothing else: no clothes, accessories or body parts, no maps, no other studio lists (an item pack that also adds animations stays). Partly marked mods and mixed mods say why they stay; base-game entries can only be hidden.
- **Move to mods_to_delete** moves the removable zipmods to `mods_to_delete` next to the game's `mods` folder (same subfolders), outside Sideloader's reach: they stop loading after a restart, and moving one back undoes it. Delete that folder yourself once you're sure. Scenes that used those items will load without them, like any missing mod.
- **Copy report** puts the report on the clipboard (title, marked / total, file, note).

### Tags

Every browser has tags, picked from a wheel so there's nothing to type.

- **Tag**: select one or more cards (or an animation) and press **T**, or right-click › *Tags...* (in Animations, just right-click). A ring of groups opens around the pointer: for outfits *Color*, *Theme*, *Risqué level*, *Fit*, *Use*. Hover a group and its values fan out around it; click a value to put it on (or take it off) every selected card. The wheel stays open, so you can pick *Black* and *Red*, then *Swimwear*, then *Suggestive* in one go. Esc, T, the center or a click outside closes it.
  - A value that every selected card has is lit; one that only some have is outlined. Right-click a group to clear it.
  - Groups marked *(one)*, like *Risqué level*, hold one value at a time: picking *Revealing* replaces *Safe*.
  - Color values (*Black*, *Pink*, *Navy*...) show a swatch. On thumbnails, color tags appear as dots along the bottom of the picture, other tags as a count (`#2`). The footer lists the selected card's tags.
- **Details panel** (right side of every browser): the selected card or item with its picture, name and where it's from, then every tag group as chips. Click a chip to put the tag on or take it off: the same tags as the wheel, laid out so you can see at a glance what's set and what isn't.
  - Lit chips are on; a chip with mint text is on only some of the selected cards (click to put it on all of them). Color values show a swatch. *one* marks a group that holds one value at a time, and *clear* takes a whole group off.
  - Click a group's name to fold it (folded groups list their values on one line). *Automatic* shows the tags worked out from the file (Parts, Cast, Length...), for reference. *edit list* / *edit shared* open the tag lists.
  - With several cards selected, chips apply to all of them. The chevron at the top folds the panel away (F1 › *Show details panel*); it also folds itself when the window is too narrow for the grid beside it.
- **Filter**: the **Tags** button in the top bar opens the same wheel for picking what to show: cards with *any* of the values picked in a group, and *all* the groups picked (Black or Red, and Swimwear). Counts next to each value say how many cards carry it. In file browsers the filter covers the open folder and its subfolders (open the top folder for everything); in Animations it covers every animation. The active filter shows as chips under the top bar; click one to drop it.
- **Search** also matches tag values.
- **Shared groups**: **Project** (FLOOP, plus your own), **Rating** (Love, Like, Okay, Dislike: one at a time, for whether you like a card, since most come from other people) and **Use** (Favorite, Thumbnail, Marketing, Reference) show in every browser's wheel, from one list, `HS2StudioBrowser.shared.tagset.txt` (*edit shared* in the wheel). Tag an outfit, a character, a scene and its items with the same Project and you can filter every browser for it.
- **Each browser's own groups** (starting lists; edit them with *edit list*). Each keeps the axis people usually sort folders by (theme, origin, cast, type), since not everyone sorts into folders, and adds ones that cut across folders:

| Browser | Groups you set | Automatic groups (filter wheel) |
|---|---|---|
| Outfits | Theme, Color, Risqué level, Coverage, Setting, Season | **Parts** (Top, Bottom, Bra, Panties, Gloves, Pantyhose, Socks, Shoes: the clothing slots the outfit fills), **Accessories** (None, 1-5, 6-15, 16+) |
| Characters | Origin, Role, Archetype, Hair, Skin | **Height** (Short, Average, Tall), **Bust** (Small, Medium, Large; female characters), from the body sliders in thirds |
| Scenes | Place, Mood, Theme, Risqué level | **Cast** (F, FF, FFF, M, MM, MMM, MF, FFM, MMF, 4+, None), counted from the characters stored in the scene |
| Poses | Position, Surface, Cast, Mood, Hands, Risqué level | **Picture** (Has picture, No picture), **Rig** (IK, FK body, Hands, Hands only) |
| Items | Type, Room, Style, Scale, Kit | **Source** (Base game, Mod), **Colors** (None, 1-4) |
| Animations | Type, Object, Position, Sex position, Gender, Energy, Moment | **Length** (Pose, Under 2 s, 2-10 s, Over 10 s), **Playback** (Loop, Once), **Source** (Base game, Mod) |

- **Automatic groups** are worked out from the cards themselves, so they can't be set by hand and are always right: a scene sitting in an FF folder that actually holds two women and two men shows *Cast: 4+*. File browsers read them in the background the first time (the filter bar says how many cards are left) and keep them in `BepInEx\cache\HS2StudioBrowser.<kind>.autotags.txt`, read again only when a card changes. The footer shows a card's automatic tags after its own. Search matches them too (*Pantyhose*, *FFM*, *pose*).
- Starting lists from earlier versions are updated to these when the file still holds the old list untouched; a list you've edited is left alone.
- Tags are saved in `HS2StudioBrowser.<kind>.tags.txt`, by card path (like favorites: renames, moves and Recycle Bin in the browser keep them in step) or, for animations, by the same id as favorites.

### Duplicates

Tick **Duplicates** in the top bar of a file browser to show only cards that have a copy in the open folder or its subfolders (open the top folder to check everything), set by set, largest set first.

- Cards match when their data is the same, whatever their picture or file name: an outfit, character or scene compares everything after the picture; a pose compares its bones and base animation (not its name, picture or extras), so a `.png` pose and its `.dat` twin match. Poses also match when every bone is within **1°** of another's (*similar*; F1 › Studio Browser: Poses › *Similar pose tolerance*, 0 for exact copies only).
- Each card shows its set and, on the copy to keep, **keep**: a favorite first, then the one with the most tags, then one with a picture (a `.png` over a `.dat`), then the oldest.
- Right-click empty space › **Select extra copies (keep one of each)**, then **Del**: everything but the kept copy goes to the Recycle Bin, and the stars and tags of what goes are moved to the copy that stays. Or pick by hand.
- Tested on the BED poses: the `!_AutoBackup` copies of nine poses come up as exact duplicates of the `.png` versions in `BED`, with the `.png` kept.

### Files

- **Favorites**: `BepInEx\config\HS2StudioBrowser.<kind>.favorites.txt` (`outfits`, `characters`, `scenes`, `poses`; animations and items are stored by id, see above), one card per line relative to the kind's UserData folder. Renaming or moving a card or folder in the browser keeps its star; the Recycle Bin removes it. A card renamed or moved outside the browser loses its star.
- **Settings**: F1 › *Studio Browser: Outfits* / *Characters* / *Scenes* / *Poses* / *Animations* / *Items*.

### With Studio's lists and Browser Folders

Browser Folders (`HS2_BrowserFolders.dll`) keeps doing its job behind the browsers (filtering Studio's lists, saving scenes into its current folder). Its outfit, character and scene folder windows are hidden while a browser stands in for that list (Harmony prefixes on their `OnGui` / `IsVisible`). To turn a part off for good: F1 › Browser Folders › *Enable folder browser in outfit / character / scene browser* off, then restart.

Studio's list is hidden, not closed: invisible and ignoring clicks (the Load screen's canvases are switched off the moment it's created, by a Harmony postfix on `SceneLoadScene.Awake`, so it never shows before the browser; the Pose list gets a transparent CanvasGroup), but still active, so Studio and other plugins that read it keep working. Renames, moves, duplicates and Recycle Bin are mirrored into it straight away, in memory, so Studio doesn't have to re-read every card (with ~2,700 outfits or ~1,600 characters that's seconds per re-read). With Browser Folders, its copy of the list is updated and re-filtered as well. A duplicate shows in Studio's character list only with Browser Folders' character folders turned on (or after a restart).

## Known limits

- Clothes only / Accessories only use the game's own coordinate loading, as HS2WearCustom does. Plugin data belonging to the half you keep (e.g. MoreAccessories' extra slots when loading *Clothes only*) follows the outfit file.
- Cards are sex-specific: female outfits load onto female characters, and Change only swaps characters of the sex shown.
- Cards so old they carry no Sideloader data can't be checked for missing zipmods and show no badge.
- Folders starting with `.` are hidden.
- With an IMGUI scaling plugin active, the pointer check that pauses Studio's shortcuts and the input blocking under the window use screen pixels, like other plugins' windows.
