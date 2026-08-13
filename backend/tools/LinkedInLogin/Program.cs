// LeadMine LinkedIn login helper.
//
// The server has no desktop to log in interactively, so this runs on your own
// machine instead: it opens a real, visible Chromium window, you log into
// your own LinkedIn account by hand, and this pushes the resulting session
// (cookies + local storage) straight to your LeadMine account. No LinkedIn
// credential is ever typed into, stored on, or sent to the server itself —
// only the already-authenticated session is.
//
// Each app user runs this for their own LinkedIn account — LeadMine does not
// use one shared, dedicated automation account. That spreads LinkedIn traffic
// across real, individually-owned accounts, and means each user accepts
// LinkedIn's terms-of-service risk for their own account, on their own
// behalf.
//
// Treat the connect code below like a password for the few minutes it's
// valid: anyone holding it can attach a LinkedIn session to your account.
//
// Usage: dotnet run -- --connect <token> [--api <baseUrl>]
//   Copy the whole command from the "Connect your LinkedIn account" dialog in
//   the app — it already has your token filled in. --api defaults to the
//   production API and only needs overriding for local testing.

using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

string? token = null;
var apiBaseUrl = "https://devnet.khaneazam.com";

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--connect" when i + 1 < args.Length:
            token = args[++i];
            break;
        case "--api" when i + 1 < args.Length:
            apiBaseUrl = args[++i].TrimEnd('/');
            break;
    }
}

if (string.IsNullOrWhiteSpace(token))
{
    Console.WriteLine("Usage: dotnet run -- --connect <token> [--api <baseUrl>]");
    Console.WriteLine();
    Console.WriteLine("Copy the exact command from the \"Connect your LinkedIn account\" dialog in the");
    Console.WriteLine("app — it already includes your connect code.");
    return 1;
}

Console.WriteLine("LeadMine LinkedIn login helper");
Console.WriteLine("===============================");
Console.WriteLine();
Console.WriteLine("A Chromium window is about to open. Log into your own LinkedIn account — the");
Console.WriteLine("one you're using with LeadMine's automation, and accept that it may eventually");
Console.WriteLine("get restricted for it. Once you can see your LinkedIn feed, come back to this");
Console.WriteLine("window and press Enter.");
Console.WriteLine();

using var playwright = await Playwright.CreateAsync();
await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
{
    Headless = false,
});

var context = await browser.NewContextAsync(new BrowserNewContextOptions
{
    Locale = "en-US",
    ViewportSize = new ViewportSize { Width = 1280, Height = 900 },
});

var page = await context.NewPageAsync();
await page.GotoAsync("https://www.linkedin.com/login");

Console.WriteLine("Waiting for you to finish logging in — press Enter here once you're on your feed.");
Console.ReadLine();

var storageStateJson = await context.StorageStateAsync();
await browser.CloseAsync();

Console.WriteLine();
Console.WriteLine("Sending your session to LeadMine...");

try
{
    using var http = new HttpClient { BaseAddress = new Uri(apiBaseUrl), Timeout = TimeSpan.FromSeconds(30) };

    var payload = JsonSerializer.Serialize(new { token, storageStateJson });
    using var content = new StringContent(payload, Encoding.UTF8, "application/json");

    var response = await http.PostAsync("/api/linkedin/session/by-token", content);

    if (response.IsSuccessStatusCode)
    {
        Console.WriteLine("Connected — your LinkedIn session is saved. You can close this window and go");
        Console.WriteLine("back to the app.");
        return 0;
    }

    var body = await response.Content.ReadAsStringAsync();
    Console.WriteLine($"Failed to save your session ({(int)response.StatusCode}): {body}");
    Console.WriteLine("Your connect code may have expired — generate a new one from the app and try again.");
    return 1;
}
catch (Exception ex)
{
    Console.WriteLine($"Could not reach {apiBaseUrl}: {ex.Message}");
    return 1;
}
