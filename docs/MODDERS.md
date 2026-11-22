# Studio Browser: modder support

Make your items, maps and animations look their best in Studio Browser by adding a few optional files to your zipmod. Everything here is optional: a mod without these files works exactly the same, with or without Studio Browser installed.

> **Status:** reading Quick Access Box thumbnail packs for items works today. The `studiobrowser/` picture folder, the manifest section, tags in cards and the export tool below are **planned** for an upcoming version. The format may still change slightly before then; this page will say when it's final.

## Available now

**Item pictures in Quick Access Box format.** If your zipmod includes Quick Access Box thumbnails, the Items browser shows them, and Quick Access Box doesn't need to be installed. See Quick Access Box's documentation for the naming (`GGGGGGGG-CCCCCCCC-SSSSSSSS-HASH.png` and its older name-based ids).

## Planned

### 1. Pictures in your zipmod

Put PNGs in a `studiobrowser` folder, named by the same IDs you use in your list files:

```
mymod.zipmod
  studiobrowser/maps/<map ID>.png
  studiobrowser/items/<category>-<item ID>.png
  studiobrowser/animations/<category>-<animation ID>.png
```

- **Maps:** 16:9, for example 480 × 270 or 960 × 540.
- **Items:** square, 128 × 128 or larger.
- **Animations:** 16:9, a frame that shows what the animation is.
- Studio Browser matches pictures to your zipmod through Sideloader, so the IDs only need to be unique within your mod.
- Priority: a picture the user takes themselves wins, then yours, then any picture Studio Browser makes automatically.

### 2. Names, tags and notes in `manifest.xml`

Add a `<studiobrowser>` section to your manifest:

```xml
<manifest schema-ver="1">
  <guid>com.yourname.downtown</guid>
  <name>Downtown Pack</name>
  <author>YourName</author>
  <version>1.0</version>

  <studiobrowser>
    <map id="0" name="Downtown Apartment" tags="Place:Living room, Setting:Indoor, Time of day:Night" />
    <item category="12" id="3" name="Bar stool" tags="Type:Furniture, Room:Bar" />
    <animation category="5" id="2" tags="Position:Sitting, Object:Chair" />
  </studiobrowser>
</manifest>
```

- **name** is shown in place of the list file's name (handy for machine-translated or cryptic names). Users can still rename it.
- **tags** are `Group:Value` pairs, separated by commas. They appear in the details panel and the tag filters, marked as coming from your mod. Users' own tags always win, and they can remove yours.
- Use the groups users already have where you can (see the tag lists in the [guide](GUIDE.md#tags)). A group they don't have is shown and can be added to their list.

### 3. Tags in cards

Outfit, character and scene cards will be able to carry their creator's tags in their plugin data, so people who download a card see it already tagged. Users will also be able to write their own tags into a card before sharing it.

### 4. Export for this mod

For any installed zipmod, **Export for this mod** in Studio Browser will write:

- a `studiobrowser/` folder with the pictures it has made or you've taken;
- a ready-to-paste `<studiobrowser>` manifest section with the names and tags you set.

So the workflow is: install your mod, open the browser, picture and tag everything, export, repack.

## Questions and requests

Open an issue on the [GitHub repo](https://github.com/AlbatrossLabs/HS2StudioBrowser/issues) if you're making a mod and want something added to this format.
