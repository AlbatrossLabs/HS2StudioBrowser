using System;
using System.Collections.Generic;
using System.IO;
using AIChara;
using MessagePack;
using HS2StudioBrowser.Game;
using Studio;

namespace HS2StudioBrowser.Outfits
{
    internal enum LoadMode
    {
        Full,
        ClothesOnly,
        AccessoriesOnly,
    }

    /// <summary>
    /// Puts an outfit card on characters. Full is exactly what Studio's Costume list "Load" does, so plugin
    /// data saved with the outfit comes along. Clothes only / Accessories only work like HS2WearCustom's
    /// ClothOnly / AccOnly buttons: the half you keep is copied aside, the card is loaded, and the kept half
    /// is put back before the character is rebuilt.
    /// </summary>
    internal static class OutfitLoader
    {
        /// <summary>Loads onto every target of the matching sex. Returns a short result for the status line.</summary>
        public static string Load(string path, int outfitSex, IList<OCIChar> targets, LoadMode mode)
        {
            if (!File.Exists(path)) return "That file is gone.";
            if (targets.Count == 0) return "Select a character first.";

            int loaded = 0, skipped = 0;
            var errors = new List<string>();
            foreach (var oci in targets)
            {
                if (oci?.charInfo == null) continue;
                if (oci.oiCharInfo.sex != outfitSex)
                {
                    skipped++;
                    continue;
                }
                try
                {
                    if (mode == LoadMode.Full) oci.LoadClothesFile(path);
                    else LoadPart(oci, path, mode);
                    loaded++;
                }
                catch (Exception ex)
                {
                    errors.Add($"{StudioTargets.NameOf(oci)}: {ex.Message}");
                    Log.Source.LogWarning($"Loading {Path.GetFileName(path)} on {StudioTargets.NameOf(oci)} failed: {ex}");
                }
            }

            var what = mode == LoadMode.Full ? "" : mode == LoadMode.ClothesOnly ? " (clothes only)" : " (accessories only)";
            var msg = loaded == 1 && targets.Count == 1
                ? $"Loaded{what} on {StudioTargets.NameOf(targets[0])}."
                : $"Loaded{what} on {loaded} character{(loaded == 1 ? "" : "s")}.";
            if (loaded == 0) msg = "Nothing loaded.";
            if (skipped > 0) msg += $" {skipped} skipped ({(outfitSex == 1 ? "female" : "male")} outfit).";
            if (errors.Count > 0) msg += " Failed: " + string.Join("; ", errors.ToArray());
            return msg;
        }

        private static void LoadPart(OCIChar oci, string path, LoadMode mode)
        {
            var cha = oci.charInfo;
            byte[] keptAcc = null, keptClothes = null;
            if (mode == LoadMode.ClothesOnly) keptAcc = MessagePackSerializer.Serialize(cha.nowCoordinate.accessory);
            else keptClothes = MessagePackSerializer.Serialize(cha.nowCoordinate.clothes);

            if (!cha.nowCoordinate.LoadFile(path)) throw new InvalidDataException("not an outfit card");

            if (keptAcc != null) cha.nowCoordinate.accessory = MessagePackSerializer.Deserialize<ChaFileAccessory>(keptAcc);
            if (keptClothes != null) cha.nowCoordinate.clothes = MessagePackSerializer.Deserialize<ChaFileClothes>(keptClothes);

            cha.Reload(false, true, true, true, true);
            cha.AssignCoordinate();
            if (cha.sex == 1)
            {
                cha.UpdateBustSoftnessAndGravity();
                oci.skirtDynamic = AddObjectFemale.GetSkirtDynamic(cha.objClothes);
                var fk = oci.oiCharInfo.activeFK[6];
                oci.ActiveFK(OIBoneInfo.BoneGroup.Skirt, fk, fk);
            }
        }
    }
}
