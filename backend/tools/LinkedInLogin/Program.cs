// LeadMine LinkedIn login helper.
//
// The server has no desktop to log in interactively, so this runs on your own
// machine instead: it opens a real, visible Chromium window, you log into
// your own LinkedIn account by hand, then this saves the resulting session
// (cookies + local storage) to a file you upload through the app. No
// LinkedIn credential is ever typed into, stored on, or sent to the server
// itself — only the already-authenticated session is.
//
// Each app user runs this for their own LinkedIn account and uploads the
// result under Settings > LinkedIn Account — LeadMine does not use one
// shared, dedicated automation account. That spreads LinkedIn traffic across
// real, individually-owned accounts, and means each user accepts LinkedIn's
// terms-of-service risk for their own account, on their own behalf.
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

await context.StorageStateAsync(new BrowserContextStorageStateOptions { Path = outputPath });
await browser.CloseAsync();

Console.WriteLine();
Console.WriteLine($"Saved session to: {outputPath}");
Console.WriteLine();
Console.WriteLine("Upload this file from Settings > LinkedIn Account in the app, signed in as");
Console.WriteLine("yourself — not to a server folder. Re-run this tool and re-upload whenever the");
Console.WriteLine("app reports your LinkedIn session has expired or been restricted.");
