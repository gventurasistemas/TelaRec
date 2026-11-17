using System.Drawing;
using System.Windows.Forms;

/// <summary>Janela colorida e animada para garantir conteúdo visual decodificável no smoke.</summary>
internal sealed class DeterministicVisualSource : IDisposable
{
    private readonly ManualResetEventSlim _ready = new();
    private readonly TaskCompletionSource<Exception?> _startup = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private Thread? _thread;
    private Form? _form;
    private int _phase;

    public nint Handle { get; private set; }

    public void Start(CancellationToken cancellationToken)
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "TelaRec.Smoke.Visual" };
        _thread.SetApartmentState(ApartmentState.STA);
        _thread.Start();
        _ready.Wait(cancellationToken);
        var startupError = _startup.Task.GetAwaiter().GetResult();
        if (startupError is not null) throw new InvalidOperationException("Não foi possível abrir a fonte visual do smoke.", startupError);
    }

    private void Run()
    {
        try
        {
            Application.EnableVisualStyles();
            _form = new Form
            {
                Text = "TelaRec smoke visual",
                ClientSize = new Size(640, 480),
                Location = new Point(100, 100),
                FormBorderStyle = FormBorderStyle.FixedToolWindow,
                MaximizeBox = false,
                MinimizeBox = false,
                ShowInTaskbar = false,
                TopMost = true
            };
            _form.Paint += Paint;
            using var timer = new System.Windows.Forms.Timer { Interval = 100 };
            timer.Tick += (_, _) => { _phase++; _form.Invalidate(); };
            _form.Shown += (_, _) =>
            {
                Handle = _form.Handle;
                timer.Start();
                _startup.TrySetResult(null);
                _ready.Set();
            };
            Application.Run(_form);
        }
        catch (Exception exception)
        {
            _startup.TrySetResult(exception);
            _ready.Set();
        }
    }

    private void Paint(object? sender, PaintEventArgs eventArgs)
    {
        var width = _form!.ClientSize.Width;
        var height = _form.ClientSize.Height;
        var color = Color.FromArgb(255, (_phase * 47) % 255, (_phase * 83) % 255, (_phase * 29) % 255);
        eventArgs.Graphics.Clear(color);
        using var accent = new SolidBrush(Color.FromArgb(255, 255 - color.R, 255 - color.G, 255 - color.B));
        eventArgs.Graphics.FillRectangle(accent, (_phase * 17) % Math.Max(1, width - 180), 64, 180, 160);
        eventArgs.Graphics.FillEllipse(Brushes.White, width / 2 - 48, height / 2 - 48, 96, 96);
        eventArgs.Graphics.DrawString("TelaRec smoke", SystemFonts.CaptionFont ?? SystemFonts.DefaultFont, Brushes.Black, 20, height - 42);
    }

    public void Dispose()
    {
        var form = _form;
        if (form is { IsDisposed: false }) form.BeginInvoke(form.Close);
        _thread?.Join(TimeSpan.FromSeconds(2));
        _ready.Dispose();
    }
}
