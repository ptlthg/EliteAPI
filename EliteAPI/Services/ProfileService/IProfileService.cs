﻿using EliteAPI.Models.Entities.Hypixel;

namespace EliteAPI.Services.ProfileService;

public interface IProfileService
{
    public Task<Profile?> GetProfile(string profileUuid);
    public Task<Profile?> GetPlayersProfileByName(string playerUuid, string profileName);
    public Task<Profile?> GetPlayersSelectedProfile(string playerUuid);

    public Task<ProfileMember?> GetProfileMember(string profileUuid, string playerUuid); 
    public Task<ProfileMember?> GetSelectedProfileMember(string playerUuid);
    public Task<ProfileMember?> GetProfileMemberByProfileName(string playerUuid, string profileName);

    public Task<PlayerData?> GetPlayerData(string playerUuid);
}
