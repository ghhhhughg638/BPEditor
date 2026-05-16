using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

/// <summary>
/// 支持在标签页头部显示关闭按钮的 TabControl
/// </summary>
public class CloseableTabControl : TabControl
{
    private int _hoveredTabIndex = -1;           // 当前鼠标悬停的标签页索引
    private bool _isMouseOverCloseButton = false; // 鼠标是否在关闭按钮上
    private const int CloseButtonSize = 16;
    private const int CloseButtonMargin = 4;

    public CloseableTabControl()
    {
        DrawMode = TabDrawMode.OwnerDrawFixed;
        SetStyle(ControlStyles.UserMouse | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        base.OnDrawItem(e);

        if (e.Index < 0 || e.Index >= TabCount)
            return;

        Rectangle tabRect = GetTabRect(e.Index);
        TabPage page = TabPages[e.Index];

        // 绘制背景
        using (Brush backBrush = new SolidBrush(GetTabBackColor(e.State, e.Index)))
        {
            e.Graphics.FillRectangle(backBrush, tabRect);
        }

        // 计算文本区域
        Rectangle textRect;
        bool showClose = (e.Index == _hoveredTabIndex);
        if (showClose)
        {
            // 预留关闭按钮空间
            int textWidth = tabRect.Width - CloseButtonSize - CloseButtonMargin * 2;
            textRect = new Rectangle(tabRect.X + 4, tabRect.Y, Math.Max(textWidth, 0), tabRect.Height);
        }
        else
        {
            // 不显示关闭按钮，文本占满（留左边距即可）
            textRect = new Rectangle(tabRect.X + 4, tabRect.Y, tabRect.Width - 8, tabRect.Height);
        }

        // 绘制文本
        TextRenderer.DrawText(e.Graphics, page.Text, Font, textRect,
            GetTabForeColor(e.State, e.Index),
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);

        // 绘制关闭按钮（悬停时）
        if (showClose)
        {
            Rectangle closeRect = GetCloseButtonRect(tabRect);
            Color closeColor = (_isMouseOverCloseButton && _hoveredTabIndex == e.Index) ? Color.Red : Color.DarkGray;

            // 按钮背景
            using (GraphicsPath path = GetRoundedRectangle(closeRect, 2))
            using (Brush bgBrush = new SolidBrush(Color.FromArgb(240, 240, 240)))
            {
                e.Graphics.FillPath(bgBrush, path);
                e.Graphics.DrawPath(Pens.LightGray, path);
            }

            // 绘制 "×"
            using (Font iconFont = new Font("Segoe UI", 10, FontStyle.Bold))
            using (Brush textBrush = new SolidBrush(closeColor))
            {
                SizeF textSize = e.Graphics.MeasureString("×", iconFont);
                float x = closeRect.X + (closeRect.Width - textSize.Width) / 2;
                float y = closeRect.Y + (closeRect.Height - textSize.Height) / 2;
                e.Graphics.DrawString("×", iconFont, textBrush, x, y);
            }
        }

        // 焦点虚线框
        if ((e.State & DrawItemState.Focus) != 0)
            ControlPaint.DrawFocusRectangle(e.Graphics, tabRect);
    }

    // 鼠标移动：检测悬停标签页及关闭按钮状态
    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        Point mousePos = e.Location;
        int newHoverIndex = GetTabIndexUnderMouse(mousePos);
        bool newMouseOverClose = false;

        if (newHoverIndex >= 0)
        {
            Rectangle tabRect = GetTabRect(newHoverIndex);
            newMouseOverClose = GetCloseButtonRect(tabRect).Contains(mousePos);
        }

        if (newHoverIndex != _hoveredTabIndex || newMouseOverClose != _isMouseOverCloseButton)
        {
            _hoveredTabIndex = newHoverIndex;
            _isMouseOverCloseButton = newMouseOverClose;
            Invalidate();
        }
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        base.OnMouseLeave(e);
        if (_hoveredTabIndex != -1 || _isMouseOverCloseButton)
        {
            _hoveredTabIndex = -1;
            _isMouseOverCloseButton = false;
            Invalidate();
        }
    }

    // 关键修复：重写 OnMouseDown 而不是 OnMouseClick
    protected override void OnMouseDown(MouseEventArgs e)
    {
        Point mousePos = e.Location;
        int clickedTabIndex = GetTabIndexUnderMouse(mousePos);
        if (e.Button == MouseButtons.Middle)
        {
            if (clickedTabIndex >= 0)
            {
                var args = new TabPageClosingEventArgs(TabPages[clickedTabIndex]);
                OnTabPageClosing(args);
                if (!args.Cancel)
                {
                    TabPages.RemoveAt(clickedTabIndex);
                    if (TabCount == 0)
                    {
                        _hoveredTabIndex = -1;
                        _isMouseOverCloseButton = false;
                    }
                    Invalidate();
                }
            }
            return;
        }

        if (e.Button == MouseButtons.Left)
        {
            if (clickedTabIndex >= 0)
            {
                Rectangle tabRect = GetTabRect(clickedTabIndex);
                if (GetCloseButtonRect(tabRect).Contains(mousePos))
                {
                    // 点击关闭按钮：关闭标签页，阻止基类处理（避免切换）
                    var args = new TabPageClosingEventArgs(TabPages[clickedTabIndex]);
                    OnTabPageClosing(args);
                    if (!args.Cancel)
                    {
                        TabPages.RemoveAt(clickedTabIndex);
                        if (TabCount == 0)
                        {
                            _hoveredTabIndex = -1;
                            _isMouseOverCloseButton = false;
                        }
                        Invalidate();
                    }
                    return; // 不调用 base，阻止选中切换
                }
            }
        }
        if (clickedTabIndex >= 0)
            SelectedIndex = clickedTabIndex;
    }

    public event EventHandler<TabPageClosingEventArgs> TabPageClosing;
    protected virtual void OnTabPageClosing(TabPageClosingEventArgs e)
    {
        TabPageClosing?.Invoke(this, e);
    }

    private int GetTabIndexUnderMouse(Point mousePos)
    {
        for (int i = 0; i < TabCount; i++)
        {
            if (GetTabRect(i).Contains(mousePos))
                return i;
        }
        return -1;
    }

    private Rectangle GetCloseButtonRect(Rectangle tabRect)
    {
        return new Rectangle(
            tabRect.Right - CloseButtonSize - CloseButtonMargin,
            tabRect.Y + (tabRect.Height - CloseButtonSize) / 2,
            CloseButtonSize,
            CloseButtonSize);
    }

    private Color GetTabBackColor(DrawItemState state, int index)
    {
        return (index == SelectedIndex) ? SystemColors.ControlLightLight : SystemColors.Control;
    }

    private Color GetTabForeColor(DrawItemState state, int index)
    {
        return (index == SelectedIndex) ? SystemColors.ControlText : SystemColors.GrayText;
    }

    private GraphicsPath GetRoundedRectangle(Rectangle rect, int radius)
    {
        GraphicsPath path = new GraphicsPath();
        path.AddArc(rect.X, rect.Y, radius * 2, radius * 2, 180, 90);
        path.AddArc(rect.Right - radius * 2, rect.Y, radius * 2, radius * 2, 270, 90);
        path.AddArc(rect.Right - radius * 2, rect.Bottom - radius * 2, radius * 2, radius * 2, 0, 90);
        path.AddArc(rect.X, rect.Bottom - radius * 2, radius * 2, radius * 2, 90, 90);
        path.CloseFigure();
        return path;
    }
}

public class TabPageClosingEventArgs : EventArgs
{
    public TabPage TabPage { get; }
    public bool Cancel { get; set; }
    public TabPageClosingEventArgs(TabPage page) => TabPage = page;
}
