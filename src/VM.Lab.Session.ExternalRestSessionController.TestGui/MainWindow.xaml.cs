using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using VM.Lab.Interfaces.Session;
using RestController = VM.Lab.Session.RestSessionController.ExternalRestSessionController;

namespace VM.Lab.Session.ExternalRestSessionController.TestGui;

/// <summary>
/// Window with one button per example REST command, and a log of what was sent, what the controller did
/// with it, and what was answered.
/// </summary>
public partial class MainWindow : Window
{
    private const string _baseAddress = "http://localhost:5080/api/v1";
    private const string _readMeAddress =
        "https://github.com/Videometer/VM.Lab.Interfaces.Session#rest-api-implementation-for-controlling-session";
    private const int _simulatedLightSetupAdjustmentMs = 2000;
    private const int _simulatedCaptureMs = 1500;
    private const int _simulatedProcessingMs = 1500;
    private const string _timeoutBody = "{\"timeoutSeconds\": 30}";
    private const string _lightSetupBody = "{\"lightSetupName\": \"ExampleLightSetup\", \"timeoutSeconds\": 30}";
    private const string _captureBody =
        "{\"id\": \"Sample1\", \"initials\": \"AB\", \"comments\": \"Example\", \"timeoutSeconds\": 30}";

    private static readonly ExampleCommand[] _exampleCommands =
    {
        new ExampleCommand("Check connection", HttpMethod.Get, "/connection", null),
        new ExampleCommand("Get state", HttpMethod.Get, "/state", null),
        new ExampleCommand("Get last image", HttpMethod.Get, "/last-image", null),
        new ExampleCommand("Capture", HttpMethod.Post, "/capture", _captureBody),
        new ExampleCommand("Wait for analysis complete", HttpMethod.Post, "/wait-for-analysis-complete", _timeoutBody),
        new ExampleCommand("Wait for sphere up", HttpMethod.Post, "/wait-for-sphere-up", _timeoutBody),
        new ExampleCommand("New", HttpMethod.Post, "/new", null),
        new ExampleCommand("Stop", HttpMethod.Post, "/stop", null),
        new ExampleCommand("Load light setup", HttpMethod.Post, "/light-setup/load", _lightSetupBody),
        new ExampleCommand("Save light setup", HttpMethod.Post, "/light-setup/save", _lightSetupBody),
        new ExampleCommand("Do auto light", HttpMethod.Post, "/light-setup/auto", _timeoutBody),
        new ExampleCommand("Acknowledge light setup failed", HttpMethod.Post, "/light-setup/acknowledge-failed", null)
    };

    private readonly HttpClient _httpClient = new HttpClient();
    private RestController? _controller;

    /// <summary>Read by the simulated session on the controller's threads, so it is not read from the check box itself</summary>
    private volatile bool _lightSetupAdjustmentFails;

    public MainWindow()
    {
        InitializeComponent();

        ReadMeLinkText.Text = _readMeAddress;
        foreach (var exampleCommand in _exampleCommands)
        {
            CommandButtons.Children.Add(CreateButton(exampleCommand));
        }

        Loaded += MainWindow_Loaded;
        Closed += MainWindow_Closed;
    }

    private Button CreateButton(ExampleCommand exampleCommand)
    {
        var button = new Button
        {
            Content = exampleCommand.Title,
            Height = 28,
            Margin = new Thickness(0, 0, 0, 4)
        };
        button.Click += async (_, _) =>
        {
            await Send(exampleCommand);
        };
        return button;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        // Show what the controller writes to the console, so both sides of each command can be followed here.
        Console.SetOut(new LogWriter(AppendLog));

        try
        {
            _controller = CreateControllerWithSimulatedSession();
        }
        catch (Exception ex)
        {
            AppendLog($"Could not start the controller: {ex.Message}");
        }
    }

    private RestController CreateControllerWithSimulatedSession()
    {
        var listener = new DummyExternalSessionControllerListener();
        var controller = new RestController(listener);

        listener.CaptureCalled += (_, _) =>
        {
            // The real session reports the state it is already in once more before it starts capturing.
            // The controller must not take that for the image being finished.
            controller.StateChanged(SessionState.Ready);

            Task.Run(() =>
            {
                controller.StateChanged(SessionState.Capturing);
                Thread.Sleep(_simulatedCaptureMs);
                controller.StateChanged(SessionState.Processing);
                Thread.Sleep(_simulatedProcessingMs);
                controller.StateChanged(SessionState.Ready);
            });
        };

        listener.LightSetupAdjusted += (_, _) =>
        {
            controller.StateChanged(SessionState.AdjustingLightSetup);

            Task.Run(() =>
            {
                Thread.Sleep(_simulatedLightSetupAdjustmentMs);
                if (_lightSetupAdjustmentFails)
                {
                    // The real session reports the reason before it moves to the failure state, so do the
                    // same here. The controller needs both: without the reason the caller waits out its timeout.
                    controller.ProvideErrorMessage("Simulated light setup adjustment failure");
                    controller.StateChanged(SessionState.AdjustingLightSetupFailed);
                }
                else
                {
                    controller.StateChanged(SessionState.Ready);
                }
            });
        };

        listener.AcknowledgeAdjustingLightSetupFailedCalled += (_, _) =>
        {
            controller.StateChanged(SessionState.Ready);
        };

        controller.ProvideSphereHeightProvider(new DummySphereHeightProvider());

        // Simulate that the session runner window has been opened and we have connected to the device
        controller.StateChanged(SessionState.Ready);
        return controller;
    }

    private async Task Send(ExampleCommand exampleCommand)
    {
        var bodyText = exampleCommand.Body ?? string.Empty;
        AppendLog($">>> {exampleCommand.Method} {_baseAddress}{exampleCommand.Path} {bodyText}");

        try
        {
            using var request = new HttpRequestMessage(exampleCommand.Method, _baseAddress + exampleCommand.Path);
            if (exampleCommand.Body != null)
            {
                request.Content = new StringContent(exampleCommand.Body, Encoding.UTF8, "application/json");
            }
            else if (exampleCommand.Method == HttpMethod.Post)
            {
                // A POST without a body must still send Content-Length: 0, or Windows answers 411.
                request.Content = new ByteArrayContent(Array.Empty<byte>());
            }

            using var response = await _httpClient.SendAsync(request);
            var answer = await response.Content.ReadAsStringAsync();
            AppendLog($"<<< {(int)response.StatusCode} {response.StatusCode}: {answer}");
        }
        catch (Exception ex)
        {
            AppendLog($"<<< Request failed: {ex.Message}");
        }
    }

    private void AppendLog(string line)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AppendLog(line));
            return;
        }

        Log.AppendText($"{DateTime.Now:HH:mm:ss.fff}  {line}{Environment.NewLine}");
        Log.ScrollToEnd();
    }

    private void SimulateLightSetupFailure_Changed(object sender, RoutedEventArgs e)
    {
        _lightSetupAdjustmentFails = SimulateLightSetupFailure.IsChecked == true;
    }

    private void ReadMeLink_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var openInBrowser = new ProcessStartInfo(_readMeAddress)
            {
                UseShellExecute = true
            };
            Process.Start(openInBrowser);
        }
        catch (Exception ex)
        {
            AppendLog($"Could not open {_readMeAddress}: {ex.Message}");
        }
    }

    private void MainWindow_Closed(object? sender, EventArgs e)
    {
        _controller?.Dispose();
        _httpClient.Dispose();
    }

    /// <summary>One button</summary>
    /// <param name="Title">Text on the button</param>
    /// <param name="Method">HTTP method</param>
    /// <param name="Path">Path below the base address</param>
    /// <param name="Body">JSON body. Null if the command has none.</param>
    private sealed record ExampleCommand(string Title, HttpMethod Method, string Path, string? Body);
}
