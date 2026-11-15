namespace HS2StudioBrowser
{
    /// <summary>Starting tag lists for each browser (each is written to its .tagset.txt once, then edited there).</summary>
    public static class TagSets
    {
        public const string Header =
            "# Tag wheel list. One group per line:   Name: value, value, value\n" +
            "# Put (one) after the name when only one value can apply, like a rating:   Level (one): Low, Mid, High\n" +
            "# Values named after colors (Black, Red, Pink...) get a swatch. Changes show the next time the wheel opens.\n\n";

        public const string OutfitsV1 = Header +
            "Color: Black, White, Gray, Red, Pink, Orange, Yellow, Gold, Green, Blue, Navy, Purple, Brown, Beige\n" +
            "Theme: Casual, Formal, Office, School, Sporty, Swimwear, Lingerie, Sleepwear, Uniform, Fantasy, Cosplay, Traditional, Seasonal\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n" +
            "Fit: Tight, Loose, Short, Long, Layered\n" +
            "Use: Favorite, WIP, Client, Archive\n";

        public const string CharactersV1 = Header +
            "Hair: Black, Brown, Blonde, Red, Pink, Silver, White, Blue, Purple, Green\n" +
            "Style: Cute, Elegant, Cool, Sporty, Gyaru, Goth, Natural, Fantasy, Mature\n" +
            "Body: Petite, Slim, Athletic, Curvy, Muscular\n" +
            "Role (one): Lead, Supporting, Extra\n" +
            "Use: Favorite, WIP, Client, Archive\n";

        public const string ScenesV1 = Header +
            "Place: Bedroom, Bathroom, Living room, Kitchen, Office, School, Outdoor, Beach, Night city, Fantasy\n" +
            "Mood: Romantic, Playful, Dramatic, Funny, Dark, Calm\n" +
            "Cast (one): Solo, Pair, Trio, Group\n" +
            "Status (one): Idea, WIP, Ready, Published\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n";

        public const string PosesV1 = Header +
            "Position: Standing, Sitting, Kneeling, Lying, Leaning, All fours, Bent over, Action\n" +
            "Cast (one): Solo, Pair, Group\n" +
            "Mood: Cute, Sexy, Casual, Dramatic, Shy, Confident\n" +
            "Hands: Open, Fist, Pointing, Peace, Holding\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n";

        public const string ItemsV1 = Header +
            "Type: Furniture, Prop, Light, Effect, Building, Nature, Vehicle, Weapon, Food, Toy, Clothing, Accessory, Sign\n" +
            "Room: Bedroom, Bathroom, Kitchen, Living room, Office, School, Shop, Outdoor, Beach\n" +
            "Style: Modern, Classic, Fantasy, Sci-fi, Japanese, Western, Horror\n" +
            "Color: Black, White, Gray, Red, Pink, Orange, Yellow, Green, Blue, Purple, Brown, Gold, Silver\n" +
            "Use: Favorite, WIP, Scene kit\n";

        // Earlier starting lists: a .tagset.txt still holding one of these, unedited, is updated to the current one.
        public const string AnimationsV1 = Header +
            "Type: Idle, Talk, Gesture, Dance, Walk, Sit, Lie, Reaction, H\n" +
            "Energy (one): Calm, Medium, Intense\n" +
            "Cast (one): Solo, Pair, Group\n" +
            "Use: Intro, Loop, Outro, Favorite, WIP\n";

        public const string AnimationsV2 = Header +
            "# Length (Pose / Short / Medium / Long) and Playback (Loop / Once) are added by the browser itself, in the filter wheel.\n\n" +
            "Object: None, Bed, Sofa, Chair, Table, Desk, Wall, Floor, Bath, Pole, Car\n" +
            "Position: Standing, Sitting, Kneeling, Lying, All fours, Leaning, Squatting, Bent over, Lifted\n" +
            "Sex position: Missionary, Cowgirl, Reverse cowgirl, Doggy, Spooning, Standing sex, Oral, Handjob, Paizuri, Masturbation, Fingering, 69, Kissing\n" +
            "Gender (one): Female, Male, F + M, F + F, M + M, Group\n" +
            "Type: Idle, Talk, Gesture, Dance, Walk, Reaction, Foreplay, H, Climax\n" +
            "Energy (one): Calm, Medium, Intense\n" +
            "Use: Intro, Loop, Outro, Favorite, WIP\n";

        // ------------------------------------------------------------------ current lists (0.13)

        /// <summary>
        /// Groups in every browser's wheel, from HS2StudioBrowser.shared.tagset.txt, so a project or a status
        /// means the same thing on outfits, characters, scenes, poses, items and animations.
        /// </summary>
        public const string Shared =
            "# Shared tag groups: these show in every Studio Browser's tag wheel (outfits, characters, scenes, poses, items, animations),\n" +
            "# so tagging an outfit, a character and a scene with the same Project lets you filter all of them for it.\n" +
            "# Same format as the other lists:   Name: value, value     (one) after the name for one value at a time.\n\n" +
            "Project: FLOOP, Project B, Project C\n" +
            "Status (one): Idea, WIP, Ready, Published, Archive\n" +
            "Use: Favorite, Thumbnail, Marketing, Reference\n";

        public const string Outfits = Header +
            "# Automatic groups (in the filter wheel, worked out from each card): Parts (which clothing slots are used), Accessories (how many).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Theme: Casual, Formal, Evening, Office, School, Sporty, Swimwear, Lingerie, Sleepwear, Uniform, Fantasy, Sci-fi, Cosplay, Traditional, Seasonal\n" +
            "Color: Black, White, Gray, Red, Pink, Orange, Yellow, Gold, Green, Blue, Navy, Purple, Brown, Beige\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n" +
            "Coverage (one): Full, Partial, Minimal\n" +
            "Setting: Indoor, Outdoor, Beach, Night out, Office, Home, Gym\n" +
            "Season: Spring, Summer, Autumn, Winter\n";

        public const string Characters = Header +
            "# Automatic groups (in the filter wheel, worked out from each card): Height, Bust (from the body sliders).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Origin (one): Original, Downloaded, Community, Modpack, Variant, Fan art\n" +
            "Role: Lead, Supporting, Extra, Background\n" +
            "Archetype: Cute, Elegant, Cool, Sporty, Tomboy, Gyaru, Goth, Mature, Fantasy\n" +
            "Hair: Black, Brown, Blonde, Red, Pink, Silver, White, Blue, Purple, Green\n" +
            "Skin (one): Fair, Tan, Dark, Fantasy\n";

        public const string Scenes = Header +
            "# Automatic groups (in the filter wheel, worked out from each scene): Cast (who is in it: F, MF, FFM...).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Place: Bedroom, Bathroom, Living room, Kitchen, Office, School, Gym, Hotel, Clinic, Outdoor, Beach, Night city, Fantasy\n" +
            "Mood: Romantic, Playful, Dramatic, Funny, Dark, Calm\n" +
            "Theme: Romance, Rough, BDSM, Group, Cosplay, Corruption, Holiday, Story\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n";

        public const string Poses = Header +
            "# Automatic groups (in the filter wheel, worked out from each pose): Picture (has one or not), Rig (IK, FK body, Hands).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Position: Standing, Sitting, Kneeling, Lying, Leaning, All fours, Bent over, Squatting, Action\n" +
            "Surface: Floor, Bed, Chair, Sofa, Table, Wall, Stairs, Bath, Car\n" +
            "Cast (one): Solo, Couple, Group\n" +
            "Mood: Cute, Sexy, Casual, Dramatic, Shy, Confident\n" +
            "Hands: Open, Fist, Pointing, Peace, Holding\n" +
            "Risqué level (one): Safe, Suggestive, Revealing, Explicit\n";

        public const string Items = Header +
            "# Automatic groups (in the filter wheel): Source (base game or a mod), Colors (how many colors the item takes).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Type: Furniture, Prop, Light, Effect, Building, Nature, Vehicle, Weapon, Food, Toy, Clothing, Accessory, Sign\n" +
            "Room: Bedroom, Bathroom, Kitchen, Living room, Office, School, Shop, Bar, Gym, Outdoor, Beach\n" +
            "Style: Modern, Classic, Japanese, Western, Fantasy, Sci-fi, Horror\n" +
            "Scale (one): Small prop, Furniture, Set piece, Environment\n" +
            "Kit: Bedroom set, Bar, Lab, Office set, Bathroom set\n";

        public const string Animations = Header +
            "# Automatic groups (in the filter wheel, measured): Length (Pose, Under 2 s, 2-10 s, Over 10 s), Playback (Loop, Once), Source (base game or a mod).\n" +
            "# Shared groups (Project, Status, Use) come from HS2StudioBrowser.shared.tagset.txt.\n\n" +
            "Type: Idle, Talk, Gesture, Dance, Walk, Reaction, H\n" +
            "Object: None, Bed, Sofa, Chair, Table, Desk, Wall, Floor, Bath, Pole, Car\n" +
            "Position: Standing, Sitting, Kneeling, Lying, All fours, Leaning, Squatting, Bent over, Lifted\n" +
            "Sex position: Missionary, Cowgirl, Reverse cowgirl, Doggy, Spooning, Standing sex, Oral, Handjob, Paizuri, Masturbation, Fingering, 69, Kissing\n" +
            "Gender (one): Female, Male, F + M, F + F, M + M, Group\n" +
            "Energy (one): Calm, Medium, Intense\n" +
            "Moment: Intro, Loop, Climax, Outro\n";
    }
}
