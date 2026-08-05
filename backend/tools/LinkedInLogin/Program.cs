// LeadMine LinkedIn login helper.
//
// The server has no desktop to log in interactively, so this runs on your own
// machine instead: it opens a real, visible Chromium window, you log into the
// LinkedIn account LeadMine is dedicating to browser automation by hand, then
// this saves the resulting session (cookies + local storage) to a file you
// upload to the server. No LinkedIn credential is ever typed into, stored on,
// or sent to the server itself — only the already-authenticated session is.
//
// Treat the output file like a password: anyone holding it is logged into
// that LinkedIn account.
//
// Usage: dotnet run [-- <output-path>]   (defaults to ./storageState.json)

using Microsoft.Playwright;

var outputPath = Path.GetFullPath(args.Length > 0 ? args[0] : "storageState.json");

Console.WriteLine("LeadMine LinkedIn login helper");
Console.WriteLine("===============================");
Console.WriteLine();
Console.WriteLine("A Chromium window is about to open. Log into the LinkedIn account you are");
Console.WriteLine("dedicating to LeadMine's automation — the same one you've accepted may");
Console.WriteLine("eventually get restricted for it. Once you can see your LinkedIn feed,");
Console.WriteLine("come back to this window and press Enter.");
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

await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = outputPath });
await browser.CloseAsync();

Console.WriteLine();
Console.WriteLine($"Saved session to: {outputPath}");
Console.WriteLine();
Console.WriteLine("Upload this file to the server at the path configured in Scraper:LinkedInStorageStatePath");
Console.WriteLine("(default: App_Data/linkedin-session.json). Re-run this tool and re-upload whenever the");
Console.WriteLine("server reports the LinkedIn session has expired.");
