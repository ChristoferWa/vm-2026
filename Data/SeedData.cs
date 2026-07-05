using VmTips.Web.Models;

namespace VmTips.Web.Data;

public static class SeedData
{
    public static void EnsureSeeded(AppDbContext db, ILogger logger)
    {
        if (db.Participants.Any())
        {
            EnsureKnownAdmins(db);
            EnsureTeamFlags(db);
            logger.LogInformation("Seed data already exists");
            return;
        }

        logger.LogInformation("Inserting first version seed data");

        var teams = new[]
        {
            new Team { Name = "Brazil", Code = "BRA", FlagEmoji = "🇧🇷" },
            new Team { Name = "Croatia", Code = "CRO", FlagEmoji = "🇭🇷" },
            new Team { Name = "Argentina", Code = "ARG", FlagEmoji = "🇦🇷" },
            new Team { Name = "Mexico", Code = "MEX", FlagEmoji = "🇲🇽" },
            new Team { Name = "France", Code = "FRA", FlagEmoji = "🇫🇷" },
            new Team { Name = "Japan", Code = "JPN", FlagEmoji = "🇯🇵" },
            new Team { Name = "Spain", Code = "ESP", FlagEmoji = "🇪🇸" },
            new Team { Name = "Germany", Code = "GER", FlagEmoji = "🇩🇪" },
            new Team { Name = "Portugal", Code = "POR", FlagEmoji = "🇵🇹" },
            new Team { Name = "Uruguay", Code = "URU", FlagEmoji = "🇺🇾" }
        };
        db.Teams.AddRange(teams);
        db.SaveChanges();

        var byCode = db.Teams.ToDictionary(x => x.Code);
        var kickoff = new DateTime(2026, 6, 10, 18, 0, 0, DateTimeKind.Utc);

        db.Matches.AddRange(
            CreateMatch(1, kickoff, "Group A", byCode["BRA"], byCode["CRO"], MatchStatus.Finished, 2, 1),
            CreateMatch(2, kickoff.AddHours(3), "Group A", byCode["ARG"], byCode["MEX"], MatchStatus.Finished, 3, 1),
            CreateMatch(3, kickoff.AddDays(1), "Group B", byCode["FRA"], byCode["JPN"], MatchStatus.Finished, 1, 0),
            CreateMatch(4, kickoff.AddDays(1).AddHours(3), "Group B", byCode["ESP"], byCode["GER"], MatchStatus.Scheduled),
            CreateMatch(5, kickoff.AddDays(2).AddHours(3), "Group C", byCode["POR"], byCode["URU"], MatchStatus.Scheduled)
        );

        db.Participants.AddRange(
        new Participant { DisplayName = "Christofer", AccessCode = "CHRIS", IsAdmin = true },
        new Participant { DisplayName = "Gordon", AccessCode = "GORDON" },
        new Participant { DisplayName = "Jeppe", AccessCode = "JEPPE" },
        new Participant { DisplayName = "Yazmine", AccessCode = "YAZMINE" },
        new Participant { DisplayName = "Mika", AccessCode = "MIKA" },

        // Add the rest of your participants here.
        new Participant { DisplayName = "Lasse", AccessCode = "Lasse" },
        new Participant { DisplayName = "Gina", AccessCode = "GINA" },
        new Participant { DisplayName = "Fredde", AccessCode = "FREDDE" },
        new Participant { DisplayName = "Elliott", AccessCode = "ELLIOTT" },
        new Participant { DisplayName = "Petra", AccessCode = "PETRA" },
        new Participant { DisplayName = "Kristian", AccessCode = "KRISTIAN" },
        new Participant { DisplayName = "Erik", AccessCode = "ERIK" },
        new Participant { DisplayName = "Mli", AccessCode = "MLI" },
        new Participant { DisplayName = "Chatgpt", AccessCode = "CHATGPT" }
        );
        db.SaveChanges();

        EnsureKnownAdmins(db);

        var user = db.Participants.Single(x => x.AccessCode == "CHRIS");
        foreach (var match in db.Matches.ToList())
        {
            db.Predictions.Add(new Prediction
            {
                ParticipantId = user.Id,
                MatchId = match.Id,
                HomeGoals = match.Id % 2 == 0 ? 3 : 2,
                AwayGoals = 1
            });
        }
        db.SaveChanges();

        if (!db.AppSettings.Any())
        {
            db.AppSettings.Add(new AppSettings
            {
                PredictionsLocked = false
            });

            db.SaveChanges();
        }

        EnsureTeamFlags(db);
    }

    private static void EnsureKnownAdmins(AppDbContext db)
    {
        var knownAdmins = db.Participants
            .Where(x => x.AccessCode.Trim().ToUpper() == "CHRIS" || x.DisplayName.Trim().ToUpper() == "CHRISTOFER")
            .ToList();

        foreach (var participant in knownAdmins)
        {
            participant.IsAdmin = true;
        }

        if (knownAdmins.Count > 0)
            db.SaveChanges();
    }

    private static void EnsureTeamFlags(AppDbContext db)
    {
        var teams = db.Teams.ToList();
        var updated = false;

        foreach (var team in teams)
        {
            var flag = TeamFlags.GetFlag(team.Name);

            if (!string.IsNullOrWhiteSpace(flag) && team.FlagEmoji != flag)
            {
                team.FlagEmoji = flag;
                updated = true;
            }
        }

        if (updated)
            db.SaveChanges();
    }

    private static Match CreateMatch(int matchNo, DateTime kickoffUtc, string groupName, Team home, Team away, MatchStatus status, int? homeGoals = null, int? awayGoals = null)
    {
        return new Match
        {
            MatchNo = matchNo,
            KickoffUtc = kickoffUtc,
            GroupName = groupName,
            HomeTeamId = home.Id,
            AwayTeamId = away.Id,
            Status = status,
            HomeGoals = homeGoals,
            AwayGoals = awayGoals
        };
    }
}
