using System;
using DeskPill;

class IntegrationTests
{
    static int n;
    static void Check(bool ok, string message)
    {
        n++;
        if (!ok)
            throw new Exception(message);
    }

    static void Main(string[] args)
    {
        var ev = CalendarService.Parse("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nSUMMARY:Hello\\, world\r\n folded\r\nDTSTART:20261126T120000Z\r\nDTEND:20261126T130000Z\r\nRRULE:FREQ=DAILY\r\nEND:VEVENT\r\nBEGIN:VEVENT\r\nSUMMARY:Day\r\nDTSTART;VALUE=DATE:20261127\r\nEND:VEVENT\r\nEND:VCALENDAR");
        Check(ev.Count == 2, "calendar count");
        Check(ev[0].Title == "Hello, worldfolded", "folding/escapes");
        Check(ev[0].StartsAt.ToUniversalTime().Hour == 12, "UTC");
        Check(ev[0].Note.Length > 0, "recurrence warning");
        Check(ev[1].AllDay && ev[1].EndsAt == ev[1].StartsAt.AddDays(1), "all day default");
        var tz = CalendarService.Parse("BEGIN:VEVENT\nDTSTART;TZID=Asia/Dhaka:20261126T120000\nEND:VEVENT");
        Check(tz.Count == 1 && tz[0].StartsAt.ToUniversalTime().Hour == 6, "TZID conversion");
        Check(CalendarService.Parse("BEGIN:VEVENT\nDTSTART:garbage\nEND:VEVENT").Count == 0, "invalid event");
        var l = LyricsService.Parse("[offset:500]\n[00:01.25][00:03.00]Hello\n[00:05]World");
        Check(l.Count == 3 && l[0].Seconds == 1.75 && l[1].Seconds == 3.5, "LRC timestamps offset");
        Check(LyricsService.Parse("Hello\nWorld")[0].Seconds == -1, "plain lyrics");
        var a = new AskService();
        Check(a.AskAsync("2+3*4", new Settings()).Result == "14", "offline math");
        Check(a.AskAsync("hello", new Settings()).Result.Contains("key"), "missing key");
        string protectedKey = AskService.ProtectKey("fixture-only");
        Check(protectedKey != "fixture-only" && AskService.UnprotectKey(protectedKey) == "fixture-only", "DPAPI roundtrip");
        Check(AskService.UnprotectKey("bad") == "", "bad key");
        foreach (string method in new[]
        {
            "MWL",
            "ISNA",
            "Karachi",
            "UmmAlQura"
        }

        )
        {
            var p = PrayerService.Calculate(new DateTime(2026, 11, 26), 23.8103, 90.4125, method);
            Check(p.Count == 6, "prayers " + method);
            for (int i = 1; i < p.Count; i++)
                Check(p[i].Time > p[i - 1].Time, "prayer order");
        }

        Check(PrayerService.Calculate(DateTime.Today, double.NaN, 0, "MWL").Count == 0, "NaN");
        Check(PrayerService.Calculate(DateTime.Today, 91, 0, "MWL").Count == 0, "invalid lat");
        Check(PrayerService.Calculate(new DateTime(2026, 6, 21), 90, 0, "MWL").Count == 0, "polar");
        Check(CalendarService.Parse("BEGIN:VEVENT\nDTSTART:20261126T120000\nSUMMARY:Event\nBEGIN:VALARM\nSUMMARY:Alarm\nEND:VALARM\nEND:VEVENT")[0].Title == "Event", "alarm must not override summary");
        Check(CalendarService.Parse("BEGIN:VEVENT\nDTSTART:20261126T120000\nSTATUS:CANCELLED\nEND:VEVENT").Count == 0, "cancelled event omitted");
        Check(a.CurrencyAsync(double.NaN, "EUR", "USD").Result.Contains("finite"), "invalid currency amount");
        Check(a.WeatherAsync(91, 0).Result.Contains("valid"), "invalid weather coordinates");
        string file = System.IO.Path.GetTempFileName();
        try
        {
            System.IO.File.WriteAllText(file, "BEGIN:VEVENT\nDTSTART;VALUE=DATE:20261126\nEND:VEVENT");
            Check(new CalendarService().LoadAsync(file).Result.Count == 1, "async local calendar");
        }
        finally
        {
            System.IO.File.Delete(file);
        }

        bool rejected = false;
        try
        {
            new CalendarService().LoadAsync("http://example.com/calendar.ics").Wait();
        }
        catch (AggregateException)
        {
            rejected = true;
        }

        Check(rejected, "insecure calendar rejected");
        var api = IntegrationHttp.Json("{\"candidates\":[{\"content\":{\"parts\":[{\"text\":\"fixture answer\"}]}}]}");
        var candidates = IntegrationHttp.Array(IntegrationHttp.Value(api, "candidates"));
        Check(candidates.Length == 1, "provider JSON arrays");
        var content = IntegrationHttp.Obj(IntegrationHttp.Value(IntegrationHttp.Obj(candidates[0]), "content"));
        Check(IntegrationHttp.Str(IntegrationHttp.Obj(IntegrationHttp.Array(IntegrationHttp.Value(content, "parts"))[0]), "text") == "fixture answer", "Gemini response shape");
        Console.WriteLine("Integration fixtures passed: " + n);
        if (args.Length > 0 && args[0] == "--live")
        {
            string currency = a.CurrencyAsync(10, "EUR", "USD").Result;
            Console.WriteLine("Public currency probe: " + currency);
            string weather = a.WeatherAsync(23.8103, 90.4125).Result;
            Console.WriteLine("Public weather probe: " + weather);
            string lyrics = new LyricsService().LoadAsync("Never Gonna Give You Up", "Rick Astley", 213).Result;
            Console.WriteLine("Public LRCLIB probe: " + (lyrics.Contains("[00:") ? "timestamped lyrics received (content not printed)" : lyrics));
        }
    }
}
