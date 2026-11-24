using System;
using System.Drawing;
using System.Windows.Forms;

namespace CRMManager.XrmToolBox
{
    internal static class ToolUi
    {
        internal static readonly Color Ink = Color.FromArgb(23, 43, 77);
        internal static readonly Color Accent = Color.FromArgb(21, 94, 239);
        internal static readonly Color Canvas = Color.FromArgb(245, 247, 251);

        internal static Button Button(string text, Action action, bool primary = false)
        {
            var button = new Button
            {
                Text = text, AutoSize = true, Height = 34, MinimumSize = new Size(80, 34),
                Padding = new Padding(10, 4, 10, 4), Margin = new Padding(0, 0, 8, 8),
                FlatStyle = FlatStyle.Flat, BackColor = primary ? Accent : Color.White,
                ForeColor = primary ? Color.White : Ink, Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = Color.FromArgb(223, 229, 239);
            button.Click += (sender, e) => action();
            return button;
        }

        internal static FlowLayoutPanel Toolbar() => new FlowLayoutPanel
        {
            Dock = DockStyle.Top, AutoSize = true, WrapContents = true,
            Padding = new Padding(0, 8, 0, 4)
        };

        internal static Label Hint(string text) => new Label
        {
            Text = text, AutoSize = false, Dock = DockStyle.Top, Height = 44,
            Padding = new Padding(4, 8, 4, 4), ForeColor = Color.FromArgb(82, 98, 122)
        };

        internal static DataGridView Grid(params (string Name, string Property)[] columns)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill, ReadOnly = true, AutoGenerateColumns = false,
                AllowUserToAddRows = false, AllowUserToDeleteRows = false,
                MultiSelect = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                RowHeadersVisible = false, BackgroundColor = Color.White,
                BorderStyle = BorderStyle.None, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            grid.RowTemplate.Height = 32;
            foreach (var column in columns)
                grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = column.Name, DataPropertyName = column.Property });
            return grid;
        }

        internal static void ConfigureForm(Form form, string title, int width, int height)
        {
            form.Text = title;
            form.Size = new Size(width, height);
            form.MinimumSize = new Size(Math.Min(width, 650), 460);
            form.StartPosition = FormStartPosition.CenterParent;
            form.Font = new Font("Segoe UI", 9);
            form.BackColor = Canvas;
            form.ForeColor = Ink;
            form.ShowInTaskbar = false;
            form.Icon = CrmManagerPlugin.LoadIcon();
        }
    }
}
