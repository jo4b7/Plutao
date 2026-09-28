namespace Plutao;

public sealed class CollectionPickerForm : Form
{
    private readonly IReadOnlyList<CollectionMediaItem> _items;
    private readonly CheckedListBox _list = new()
    {
        Dock = DockStyle.Fill,
        CheckOnClick = true,
        IntegralHeight = false,
        BackColor = Color.FromArgb(24, 24, 24),
        ForeColor = Color.FromArgb(240, 240, 240),
        BorderStyle = BorderStyle.FixedSingle
    };
    private readonly Label _summary = new() { AutoSize = true, ForeColor = Color.FromArgb(210, 210, 210) };
    private readonly Button _all = new() { Text = "Marcar todos", AutoSize = true };
    private readonly Button _none = new() { Text = "Desmarcar todos", AutoSize = true };
    private readonly Button _ok = new() { Text = "USAR SELECIONADOS", AutoSize = true };
    private readonly Button _cancel = new() { Text = "Cancelar", AutoSize = true };

    public IReadOnlyList<int> SelectedIndexes
        => Enumerable.Range(0, _list.Items.Count)
            .Where(i => _list.GetItemChecked(i))
            .Select(i => _items[i].Index)
            .ToArray();

    public CollectionPickerForm(IReadOnlyList<CollectionMediaItem> items)
    {
        _items = items;
        Text = "Plutao - Selecionar vídeos";
        Width = 860;
        Height = 650;
        MinimumSize = new Size(620, 450);
        StartPosition = FormStartPosition.CenterParent;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(10, 10, 10);
        ForeColor = Color.FromArgb(240, 240, 240);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            Padding = new Padding(14),
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var top = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = true };
        top.Controls.Add(_summary);
        top.Controls.Add(_all);
        top.Controls.Add(_none);
        root.Controls.Add(top, 0, 0);

        foreach (var item in items)
            _list.Items.Add(item.DisplayText, true);
        root.Controls.Add(_list, 0, 1);

        var bottom = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            Padding = new Padding(0, 10, 0, 0)
        };
        bottom.Controls.Add(_cancel);
        bottom.Controls.Add(_ok);
        root.Controls.Add(bottom, 0, 2);
        Controls.Add(root);

        foreach (var button in new[] { _all, _none, _ok, _cancel })
        {
            button.BackColor = Color.FromArgb(28, 28, 28);
            button.ForeColor = Color.FromArgb(240, 240, 240);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(62, 62, 62);
        }

        _all.Click += (_, _) => SetAll(true);
        _none.Click += (_, _) => SetAll(false);
        _list.ItemCheck += (_, _) => BeginInvoke(new Action(UpdateSummary));
        _ok.Click += (_, _) =>
        {
            if (SelectedIndexes.Count == 0)
            {
                MessageBox.Show(this, "Selecione pelo menos um vídeo.", "Plutao", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DialogResult = DialogResult.OK;
            Close();
        };
        _cancel.Click += (_, _) => { DialogResult = DialogResult.Cancel; Close(); };
        AcceptButton = _ok;
        CancelButton = _cancel;
        UpdateSummary();
    }

    private void SetAll(bool value)
    {
        for (var i = 0; i < _list.Items.Count; i++)
            _list.SetItemChecked(i, value);
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        var selected = Enumerable.Range(0, _list.Items.Count).Count(i => _list.GetItemChecked(i));
        _summary.Text = $"Encontrados: {_items.Count}   •   Selecionados: {selected}";
    }
}
