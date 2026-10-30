using LolDataExtractor.Core;

namespace LolDataExtractor.Tests;

public class TextureBindingTests
{
    [Fact]
    public void Tails_get_their_own_texture_while_the_body_keeps_the_main_one()
    {
        var parts = new[] { "Body", "Eyes", "Tails", "Tail_Large" };
        var textures = new[] { "ahri_base_tx_cm", "ahri_base_tails_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "ahri_base");

        Assert.Equal(new[] { 0, 0, 1, 1 }, plan);
    }

    [Fact]
    public void Load_screens_and_effect_swaps_never_bind()
    {
        var parts = new[] { "Body", "Tails" };
        var textures = new[]
        {
            "ahri_base_tx_cm", "ahriloadscreen_0", "ahri_base_babyfox_tx_cm", "ahri_base_tails_tx_cm",
        };

        var plan = TextureBinder.Assign(parts, textures, "ahri_base");

        Assert.Equal(new[] { 0, 3 }, plan);
    }

    [Fact]
    public void ASingle_texture_covers_every_part()
    {
        var plan = TextureBinder.Assign(new[] { "Body", "Tails" }, new[] { "aatrox_base_tx_cm" }, "aatrox_base");

        Assert.Equal(new[] { 0, 0 }, plan);
    }

    [Fact]
    public void Without_any_texture_nothing_is_bound()
    {
        var plan = TextureBinder.Assign(new[] { "Body", "Tails" }, Array.Empty<string>(), "ahri_base");

        Assert.Equal(new[] { TextureBinder.NoTexture, TextureBinder.NoTexture }, plan);
    }

    [Fact]
    public void Part_names_with_suffixes_still_match_their_texture()
    {
        var parts = new[] { "Sword_01", "Body" };
        var textures = new[] { "yasuo_base_tx_cm", "yasuo_base_sword_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "yasuo_base");

        Assert.Equal(new[] { 1, 0 }, plan);
    }

    [Fact]
    public void Plural_and_singular_part_names_match_each_other()
    {
        var plan = TextureBinder.Assign(new[] { "Tail" }, new[] { "ahri_base_tx_cm", "ahri_base_tails_tx_cm" }, "ahri_base");

        Assert.Equal(1, plan[0]);
    }

    [Fact]
    public void ADifferent_model_name_falls_back_to_the_first_colour_texture()
    {
        var plan = TextureBinder.Assign(new[] { "Body" }, new[] { "akali_base_tx_cm", "akali_base_weapon_tx_cm" }, "unknown");

        Assert.Equal(0, plan[0]);
    }

    [Fact]
    public void Masks_and_roughness_maps_are_never_drawn()
    {
        // Ahri's Arcana skin keeps the body colour map next to a body mask and a roughness map.
        var parts = new[] { "Body", "Tails" };
        var textures = new[]
        {
            "ahri_skin65_tx_cm", "ahri_skin65_bodymask_tx_cm", "ahri_skin89_rmsa_tx_cm",
            "ahriloadscreen_65_le", "common_color-bellcurve",
        };

        var plan = TextureBinder.Assign(parts, textures, "ahri_skin65");

        Assert.Equal(new[] { 0, 0 }, plan);
    }

    [Fact]
    public void Accessory_textures_win_over_the_main_one_while_masked_variants_do_not()
    {
        // The ult and fish parts have their own colour map; every masked variant of them must lose.
        var parts = new[] { "Body", "Ultcloth01", "Fishbody", "Mtails" };
        var textures = new[]
        {
            "ahri_skin89_tx_cm", "ahri_skin89_ult_turbuldeform_mask_tx_cm", "ahri_skin89_ult_tx_cm",
            "ahri_skin89_tails_mask", "ahri_skin89_tails_rmsa_tx_cm", "ahri_skin89_tails_tx_cm",
            "ahri_skin89_fish_tx_cm", "ahri_skin89_scrollmask_tx_cm", "ahri_skin89_scarf_deformmask",
        };

        var plan = TextureBinder.Assign(parts, textures, "ahri_skin89");

        Assert.Equal(new[] { 0, 2, 6, 5 }, plan);
    }

    [Fact]
    public void AColour_texture_without_a_map_suffix_still_binds_by_name()
    {
        // "ahri_skin65_gold" is a colour map that carries no suffix at all.
        var parts = new[] { "Body", "Gold" };
        var textures = new[] { "ahri_skin65_tx_cm", "ahri_skin65_bodymask_tx_cm", "ahri_skin65_gold" };

        var plan = TextureBinder.Assign(parts, textures, "ahri_skin65");

        Assert.Equal(new[] { 0, 2 }, plan);
    }

    [Fact]
    public void AFolder_without_any_colour_map_binds_nothing()
    {
        // The jade classic models keep only the loading screen next to the mesh.
        var parts = new[] { "lambert5", "Body" };
        var textures = new[] { "jade_alistarloadscreen_301_le", "jade_alistarloadscreen_301" };

        var plan = TextureBinder.Assign(parts, textures, "alistair_classic");

        Assert.All(plan, index => Assert.Equal(TextureBinder.NoTexture, index));
    }

    [Fact]
    public void Numbered_variants_do_not_take_each_others_textures()
    {
        var parts = new[] { "Ezreal_Skin05_Lv1_Mat", "Ezreal_Skin05_Lv3_Mat", "Ezreal_Skin05_Head_Mat" };
        var textures = new[] { "ezreal_skin05_tx_cm", "ezreal_skin05_lv3_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "ezreal_skin05");

        Assert.Equal(new[] { 0, 1, 0 }, plan);
    }

    [Fact]
    public void Glued_part_names_are_split_into_words()
    {
        var parts = new[] { "Tailset1", "Fishbody", "Hair" };
        var textures = new[] { "ahri_skin66_tx_cm", "ahri_skin66_tails_tx_cm", "ahri_skin66_fish_tx_cm", "ahri_skin66_hair_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "ahri_skin66");

        Assert.Equal(new[] { 1, 2, 3 }, plan);
    }

    [Fact]
    public void TheModelOwnTexture_counts_even_when_its_name_reads_like_a_mask()
    {
        // Brand's Gas Mask skin: "brand_gasmask_tx_cm" is the colour map of the gas mask.
        var parts = new[] { "riotRig:brand_gasmask_MD:brand_gasmask_shade" };
        var textures = new[] { "brandloadscreen_1", "brand_gasmask_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "brand_gasmask");

        Assert.Equal(new[] { 1 }, plan);
    }

    [Fact]
    public void TheBody_texture_becomes_the_default_one()
    {
        // Lillia's Winter skin has a texture per sub-object and no main file; the body one wins.
        var parts = new[] { "Head_Bloom", "Hair" };
        var textures = new[]
        {
            "lillia_skin28_bird_tx_cm", "lillia_skin28_weapon_tx_cm", "lillia_skin28_body_tx_cm",
        };

        var plan = TextureBinder.Assign(parts, textures, "lillia_skin28");

        Assert.Equal(new[] { 2, 2 }, plan);
    }

    [Fact]
    public void ACamel_case_letter_digit_boundary_keeps_the_number_with_its_word()
    {
        // "Body01" and "Body02" are different parts; neither may take the other's texture.
        var parts = new[] { "Eyes01", "Eyes02" };
        var textures = new[] { "x_base_tx_cm", "x_base_eyes02_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "x_base");

        Assert.Equal(new[] { 0, 1 }, plan);
    }

    [Fact]
    public void Generic_body_words_do_not_stop_a_part_texture_from_binding()
    {
        // Gwen's doll map carries "main" in its name although it belongs to the Doll submesh.
        var parts = new[] { "Body", "Doll" };
        var textures = new[] { "gwen_base_main_tx_cm", "gwen_base_main_doll_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "gwen_base");

        Assert.Equal(new[] { 0, 1 }, plan);
    }

    [Fact]
    public void An_ability_letter_map_is_not_taken_for_a_part_texture()
    {
        // "..._r_tx_cm" is the R ability, not the hair: "hair" only ends in r.
        var parts = new[] { "Hair", "Body" };
        var textures = new[] { "janna_skin67_tx_cm", "janna_skin67_r_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "janna_skin67");

        Assert.Equal(new[] { 0, 0 }, plan);
    }

    [Fact]
    public void A_short_word_still_matches_a_part_it_starts()
    {
        // "e_tx_cm" is the eye texture and starts the part name Eyes.
        var parts = new[] { "Eyes", "Body" };
        var textures = new[] { "ahri_skin17_tx_cm", "ahri_skin17_e_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "ahri_skin17");

        Assert.Equal(new[] { 1, 0 }, plan);
    }

    [Fact]
    public void The_plainer_texture_wins_a_tie()
    {
        // "Gun_Mat" is explained by both; the plain gun map is the safer pick over the t3 variant.
        var parts = new[] { "Gun_Mat" };
        var textures = new[] { "caitlyn_skin22_tx_cm", "caitlyn_skin22_gun_t3_tx_cm", "caitlyn_skin22_gun_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "caitlyn_skin22");

        Assert.Equal(new[] { 2 }, plan);
    }

    [Fact]
    public void A_texture_that_only_adds_a_generic_word_is_left_to_the_fallback()
    {
        // "main" alone explains nothing about the part, so the model keeps its own colour map.
        var parts = new[] { "Sword" };
        var textures = new[] { "x_base_sword_tx_cm", "x_base_main_tx_cm" };

        var plan = TextureBinder.Assign(parts, textures, "x_base");

        Assert.Equal(new[] { 0 }, plan);
    }
}
