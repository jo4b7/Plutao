using System.Runtime.InteropServices;

namespace Plutao;

public sealed class FastDataGridView : DataGridView
{
    private const int WmSetRedraw = 0x000B;
    private int _redrawSuspendCount;

    public FastDataGridView()
    {
        DoubleBuffered = true;
        SetStyle(
            ControlStyles.OptimizedDoubleBuffer |
            ControlStyles.AllPaintingInWmPaint,
            true);
        UpdateStyles();
    }

    public void BeginBatchUpdate()
    {
        _redrawSuspendCount++;
        if (_redrawSuspendCount != 1 || !IsHandleCreated)
            return;

        SendMessage(Handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    public void EndBatchUpdate(bool invalidate = true)
    {
        if (_redrawSuspendCount <= 0)
            return;

        _redrawSuspendCount--;
        if (_redrawSuspendCount != 0 || !IsHandleCreated)
            return;

        SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        if (invalidate)
        {
            Invalidate(true);
            Update();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && _redrawSuspendCount > 0 && IsHandleCreated)
        {
            _redrawSuspendCount = 0;
            SendMessage(Handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        }

        base.Dispose(disposing);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr hWnd,
        int msg,
        IntPtr wParam,
        IntPtr lParam);
}
