using System.Windows;
using System.Windows.Input;
using System.Windows.Shapes;
using TelaRec.Core.Capture;

namespace TelaRec.App.Wpf;
public partial class RegionOverlayWindow : Window
{
    private Point _start;
    public CaptureTarget.Region? Result { get; private set; }
    public RegionOverlayWindow() { InitializeComponent(); KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); }; }
    private void Start(object sender, MouseButtonEventArgs e) { _start = e.GetPosition(this); Selection.Visibility = Visibility.Visible; Canvas.SetLeft(Selection, _start.X); Canvas.SetTop(Selection, _start.Y); Selection.Width = 0; Selection.Height = 0; CaptureMouse(); }
    private void Move(object sender, MouseEventArgs e) { if (!IsMouseCaptured) return; var p=e.GetPosition(this); var x=Math.Min(_start.X,p.X); var y=Math.Min(_start.Y,p.Y); Canvas.SetLeft(Selection,x); Canvas.SetTop(Selection,y); Selection.Width=Math.Abs(p.X-_start.X); Selection.Height=Math.Abs(p.Y-_start.Y); }
    private void End(object sender, MouseButtonEventArgs e) { ReleaseMouseCapture(); var x=(int)Canvas.GetLeft(Selection); var y=(int)Canvas.GetTop(Selection); Result=new CaptureTarget.Region(x,y,(int)Selection.Width,(int)Selection.Height); DialogResult=Result.IsValid; Close(); }
}
