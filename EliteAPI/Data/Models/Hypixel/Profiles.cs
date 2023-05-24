﻿using Microsoft.EntityFrameworkCore;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace EliteAPI.Data.Models.Hypixel;

public class Profile
{
    [Key] public required string ProfileId { get; set; }
    public required string ProfileName { get; set; }
    public string? GameMode { get; set; }
    public DateTime? LastSave { get; set; }
    public List<ProfileMember> Members { get; set; } = new();
    public ProfileBanking Banking { get; set; } = new();
    public List<CraftedMinion> CraftedMinions { get; set; } = new();
    public bool IsDeleted { get; set; } = false;
}

public class ProfileMember
{
    [Key] public int Id { get; set; }
    public required string PlayerUuid { get; set; }

    public List<Collection> Collections { get; set; } = new();
    public required JacobData JacobData { get; set; }
    public List<Pet> Pets { get; set; } = new();
    public List<Skill> Skills { get; set; } = new();
    public bool IsSelected { get; set; }
    public bool WasRemoved { get; set; } = false;
    public DateTime LastUpdated { get; set; } = DateTime.MinValue;

    [ForeignKey("MinecraftAccount")]
    public int MinecraftAccountId { get; set; }
    public required MinecraftAccount MinecraftAccount { get; set; }

    [ForeignKey("Profile")]
    public required string ProfileId { get; set; }
    public required Profile Profile { get; set; }
}