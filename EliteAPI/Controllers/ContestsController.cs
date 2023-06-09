﻿using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AutoMapper;
using EliteAPI.Data;
using EliteAPI.Models.DTOs.Outgoing;
using EliteAPI.Utilities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace EliteAPI.Controllers;

[Route("api/[controller]")]
[ApiController]
public class ContestsController : ControllerBase
{
    private readonly DataContext _context;
    private readonly IMapper _mapper;
    private readonly IConnectionMultiplexer _cache;
    private readonly ILogger<ContestsController> _logger;

    public ContestsController(DataContext dataContext, IMapper mapper, IConnectionMultiplexer cache, ILogger<ContestsController> logger)
    {
        _context = dataContext;
        _mapper = mapper;
        _cache = cache;
        _logger = logger;
    }

    // GET api/<ContestsController>/285
    [HttpGet("at/{year:int}")]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<YearlyContestsDto>> GetAllContestsInOneYear(int year)
    {
        var currentDate = SkyblockDate.Now;
        if (currentDate.Year == year - 1) {
            var db = _cache.GetDatabase();

            var data = await db.StringGetAsync($"contests:{currentDate.Year}");
            if (data.HasValue)
                try {
                    var sourcedContests = JsonSerializer.Deserialize<Dictionary<long, List<string>>>(data!);

                    return Ok(new YearlyContestsDto {
                        Year = currentDate.Year + 1,
                        Count = (sourcedContests?.Count ?? 0) * 3,
                        Complete = sourcedContests?.Count == 124,
                        Contests = sourcedContests ?? new Dictionary<long, List<string>>()
                    });
                }
                catch (Exception e) {
                    _logger.LogError(e, "Failed to deserialize cached contests data");
                }
        }
        
        var startTime = FormatUtils.GetTimeFromSkyblockDate(year - 1, 0, 0);
        var endTime = FormatUtils.GetTimeFromSkyblockDate(year, 0, 0);
        
        var contests = await _context.JacobContests
            .Where(j => j.Timestamp > startTime && j.Timestamp < endTime)
            .ToListAsync();

        var result = new Dictionary<long, List<string>>();
        foreach (var contest in contests) {
            if (!result.TryGetValue(contest.Timestamp, out var value)) {
                value = new List<string>();
                result.Add(contest.Timestamp, value);
            }

            var crop = FormatUtils.GetFormattedCropName(contest.Crop);

            value.Add(crop);
        }

        var dto = new YearlyContestsDto {
            Year = year,
            Count = contests.Count,
            Complete = contests.Count == 372,
            Contests = result
        };

        return Ok(dto);
    }
    
    // GET api/<ContestsController>/at/now
    [HttpGet("at/now")]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<YearlyContestsDto>> GetThisYearsContests() {
        return await GetAllContestsInOneYear(SkyblockDate.Now.Year + 1);
    }
    
    // GET api/<ContestsController>/200/12/5
    [HttpGet("at/{year:int}/{month:int}/{day:int}")]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<JacobContestWithParticipationsDto>>> GetContestsAt(int year, int month, int day) {
        if (year < 1 || month is > 12 or < 1 || day is > 31 or < 1) return BadRequest("Invalid date.");
        
        var timestamp = FormatUtils.GetTimeFromSkyblockDate(year - 1, month - 1, day - 1);

        return await GetContestsAt(timestamp);
    }

    // GET api/<ContestsController>/200/12
    [HttpGet("at/{year:int}/{month:int}")]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<Dictionary<int, List<JacobContestDto>>>> GetAllContestsInOneMonth(int year, int month)
    {
        if (year < 1 || month is > 12 or < 1) return BadRequest("Invalid date.");
        
        var startTime = FormatUtils.GetTimeFromSkyblockDate(year - 1, month - 1, 0);
        var endTime = FormatUtils.GetTimeFromSkyblockDate(year - 1, month, 0);

        var contests = await _context.JacobContests
            .Where(j => j.Timestamp >= startTime && j.Timestamp < endTime)
            .ToListAsync();

        var mappedContests = _mapper.Map<List<JacobContestDto>>(contests);

        var data = new Dictionary<int, List<JacobContestDto>>();

        foreach (var contest in mappedContests) {
            var skyblockDate = new SkyblockDate(contest.Timestamp);
            var day = skyblockDate.Day + 1;

            if (data.TryGetValue(day, out var value))
            {
                value.Add(contest);
            }
            else
            {
                data.Add(day, new List<JacobContestDto> { contest });
            }
        }

        return Ok(data);
    }

    // GET api/<ContestsController>/1604957700
    [HttpGet("{timestamp:long}")]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<JacobContestWithParticipationsDto>>> GetContestsAt(long timestamp)
    {
        var skyblockDate = new SkyblockDate(timestamp);
        if (skyblockDate.Year < 1) return BadRequest("Invalid skyblock date.");
        
        var contests = await _context.JacobContests
            .Where(j => j.Timestamp == timestamp)
            .ToListAsync();

        if (contests.Count == 0) return new List<JacobContestWithParticipationsDto>();
        
        var data = _mapper.Map<List<JacobContestWithParticipationsDto>>(contests);

        foreach (var contest in contests)
        {
            var participations = await _context.ContestParticipations
                .Where(p => p.JacobContestId == contest.Id)
                .Include(p => p.ProfileMember.MinecraftAccount)
                .ToListAsync();

            var crop = FormatUtils.GetFormattedCropName(contest.Crop);

            var stripped = _mapper.Map<List<StrippedContestParticipationDto>>(participations);
           
            data.First(d => d.Crop.Equals(crop)).Participations = stripped;
        }

        return Ok(data);
    }
    
    // GET api/contest/285:2_11:CACTUS
    [Route("/api/contest/{contestKey}")]
    [HttpGet]
    [ResponseCache(Duration = 60 * 30, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<JacobContestWithParticipationsDto>> GetContestFromKey(string contestKey) {
        var timestamp = FormatUtils.GetTimeFromContestKey(contestKey);
        var cropId = FormatUtils.GetCropFromContestKey(contestKey);

        if (timestamp == 0 || cropId is null) {
            return BadRequest("Invalid contest key");
        }
        
        var contest = await _context.JacobContests
            .Where(j => j.Timestamp == timestamp && j.Crop == cropId)
            .FirstOrDefaultAsync();
        
        if (contest is null) return NotFound("Contest not found");
        
        var data = _mapper.Map<JacobContestWithParticipationsDto>(contest);
        
        var participations = await _context.ContestParticipations
            .Where(p => p.JacobContestId == contest.Id)
            .Include(p => p.ProfileMember.MinecraftAccount)
            .ToListAsync();
        
        var stripped = _mapper.Map<List<StrippedContestParticipationDto>>(participations);
        
        data.Participations = stripped;
        return Ok(data);
    }

    // GET api/<ContestsController>/7da0c47581dc42b4962118f8049147b7/
    [HttpGet("{playerUuid}")]
    [ResponseCache(Duration = 600, Location = ResponseCacheLocation.Any)]
    public async Task<ActionResult<IEnumerable<ContestParticipationDto>>> GetAllOfOnePlayersContests(string playerUuid)
    {
        var profileMembers = await _context.ProfileMembers
            .Where(p => p.PlayerUuid.Equals(playerUuid))
            .Include(p => p.JacobData)
            .ThenInclude(j => j.Contests)
            .ThenInclude(c => c.JacobContest)
            .AsSplitQuery()
            .ToListAsync();

        if (profileMembers.Count == 0) return NotFound("Player not found.");

        var data = new List<ContestParticipationDto>();

        foreach (var profileMember in profileMembers)
        {
            data.AddRange(_mapper.Map<List<ContestParticipationDto>>(profileMember.JacobData.Contests));
        }

        return Ok(data);
    }

    // GET api/<ContestsController>/7da0c47581dc42b4962118f8049147b7/7da0c47581dc42b4962118f8049147b7
    [HttpGet("{playerUuid}/{profileUuid}")]
    public async Task<ActionResult<IEnumerable<ContestParticipationDto>>> GetAllContestsOfOneProfileMember(string playerUuid, string profileUuid)
    {
        var profileMember = await _context.ProfileMembers
            .Where(p => p.PlayerUuid.Equals(playerUuid) && p.ProfileId.Equals(profileUuid))
            .Include(p => p.JacobData)
            .ThenInclude(j => j.Contests)
            .ThenInclude(c => c.JacobContest)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (profileMember is null) return NotFound("Player not found.");

        return Ok(_mapper.Map<List<ContestParticipationDto>>(profileMember.JacobData.Contests));
    }

    // GET api/<ContestsController>/7da0c47581dc42b4962118f8049147b7/Selected
    [HttpGet("{playerUuid}/Selected")]
    public async Task<ActionResult<IEnumerable<ContestParticipationDto>>> GetAllContestsOfSelectedProfileMember(string playerUuid)
    {
        var profileMember = await _context.ProfileMembers
            .Where(p => p.PlayerUuid.Equals(playerUuid) && p.IsSelected)
            .Include(p => p.JacobData)
            .ThenInclude(j => j.Contests)
            .ThenInclude(c => c.JacobContest)
            .AsSplitQuery()
            .FirstOrDefaultAsync();

        if (profileMember is null) return NotFound("Player not found.");

        return Ok(_mapper.Map<List<ContestParticipationDto>>(profileMember.JacobData.Contests));
    }
    
        // POST api/<ContestsController>/at/now
    [HttpPost("at/now")]
    [RequestSizeLimit(16000)] // Leaves some room for error
    public async Task<ActionResult> SendThisYearsContests([FromBody] Dictionary<long, List<string>> body) {
        var currentDate = new SkyblockDate(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var currentYear = currentDate.Year;
        
        var db = _cache.GetDatabase();
        if (await db.KeyExistsAsync($"contests:{currentDate.Year}")) {
            //return Ok();
        }
        
        if (currentDate.Month > 8) {
            return BadRequest("Contests cannot be submitted this late in the year!");
        }
        
        if (body.Keys.Count != 124) {
            return BadRequest("Invalid number of contests! Expected 123, got " + body.Count);
        }
        
        // Check if any of the timestamps are invalid
        if (body.Keys.ToList().Exists(timestamp => new SkyblockDate(timestamp).Year != currentYear)) {
            return BadRequest("Invalid year! All contests must be from the current year (" + (currentYear + 1) + ")");
        }
        
        // Check if any of the crops are invalid
        if (body.Values.ToList().Exists(crops => // Check that all crops are valid and that there are no duplicates
                crops.Distinct().Count() != 3 ||
                crops.Exists(crop => FormatUtils.FormattedCropNameToCrop(crop) is null))) 
        {
            return BadRequest("Invalid contest(s)! All crops must be valid without duplicates in the same contest!");
        }

        var httpContext = HttpContext.Request.HttpContext;
        var ipAddress = httpContext.Connection.RemoteIpAddress?.ToString();
        if (string.IsNullOrEmpty(ipAddress))
        {
            return BadRequest("Invalid request");
        }

        var addressKey = IPAddress.IsLoopback(httpContext.Connection.RemoteIpAddress!) 
            ? $"contestsSubmission:{Guid.NewGuid()}" // Use a GUID for localhost so that it can be tested
            : $"contestsSubmission:{ipAddress}";

        var existingData = await db.StringGetAsync(addressKey);
        
        // Check if IP has already submitted a response
        if (!string.IsNullOrEmpty(existingData))
        {
            return BadRequest("Already submitted a response");
        }
        
        // Store that the IP has submitted a response
        await db.StringSetAsync(addressKey, "1", TimeSpan.FromHours(5));
        
        // Serialize the body to a JSON string
        var serializedData = JsonSerializer.Serialize(body);
        var hash = Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(serializedData)));

        // Increment the number of this particular response
        var hashKey = $"contestsHash:{hash}";
        await db.StringIncrementAsync(hashKey);
        await db.StringGetSetExpiryAsync(hashKey, TimeSpan.FromHours(5));
        
        //Get the current number of this particular response
        var identicalResponses = await db.StringGetAsync(hashKey);

        if (!identicalResponses.TryParse(out long val) || val < 5) return Ok($"Response saved, {val} identical responses");
        
        var secondsUntilNextYear = FormatUtils.GetTimeFromSkyblockDate(currentYear + 1, 0, 0) - DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Save the request data
        await db.StringSetAsync($"contests:{currentYear}", serializedData, TimeSpan.FromSeconds(secondsUntilNextYear));

        return Ok();
    }
}
