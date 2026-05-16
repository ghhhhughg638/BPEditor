namespace BPEditor
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            menuStrip1 = new MenuStrip();
            文件ToolStripMenuItem = new ToolStripMenuItem();
            打开ToolStripMenuItem = new ToolStripMenuItem();
            保存ToolStripMenuItem1 = new ToolStripMenuItem();
            导入ToolStripMenuItem = new ToolStripMenuItem();
            节点定义表jsonToolStripMenuItem1 = new ToolStripMenuItem();
            导出ToolStripMenuItem = new ToolStripMenuItem();
            当前文件ToolStripMenuItem = new ToolStripMenuItem();
            uAssetFileuassetToolStripMenuItem = new ToolStripMenuItem();
            uAssetJsonjsonToolStripMenuItem = new ToolStripMenuItem();
            blueprintDatajsonToolStripMenuItem = new ToolStripMenuItem();
            整个工作区pakToolStripMenuItem = new ToolStripMenuItem();
            节点定义表jsonToolStripMenuItem = new ToolStripMenuItem();
            设置ToolStripMenuItem = new ToolStripMenuItem();
            操作ToolStripMenuItem = new ToolStripMenuItem();
            扫描节点定义ToolStripMenuItem = new ToolStripMenuItem();
            帮助ToolStripMenuItem = new ToolStripMenuItem();
            使用方法ToolStripMenuItem = new ToolStripMenuItem();
            关于ToolStripMenuItem = new ToolStripMenuItem();
            tableLayoutPanel1 = new TableLayoutPanel();
            tableLayoutPanel3 = new TableLayoutPanel();
            tabControl1 = new CloseableTabControl();
            label1 = new Label();
            tabControl2 = new TabControl();
            tabPage1 = new TabPage();
            treeView1 = new TreeView();
            tabPage2 = new TabPage();
            treeView2 = new TreeView();
            tabControl3 = new TabControl();
            tabPage3 = new TabPage();
            tableLayoutPanel4 = new TableLayoutPanel();
            nodeLibrary = new ListBox();
            panel1 = new Panel();
            textBox1 = new TextBox();
            button1 = new Button();
            tabPage4 = new TabPage();
            tableLayoutPanel2 = new TableLayoutPanel();
            panel2 = new Panel();
            textBox2 = new TextBox();
            button2 = new Button();
            dataLibrary = new DataGridView();
            menuStrip1.SuspendLayout();
            tableLayoutPanel1.SuspendLayout();
            tableLayoutPanel3.SuspendLayout();
            tabControl2.SuspendLayout();
            tabPage1.SuspendLayout();
            tabPage2.SuspendLayout();
            tabControl3.SuspendLayout();
            tabPage3.SuspendLayout();
            tableLayoutPanel4.SuspendLayout();
            panel1.SuspendLayout();
            tabPage4.SuspendLayout();
            tableLayoutPanel2.SuspendLayout();
            panel2.SuspendLayout();
            ((System.ComponentModel.ISupportInitialize)dataLibrary).BeginInit();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { 文件ToolStripMenuItem, 操作ToolStripMenuItem, 帮助ToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(800, 28);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // 文件ToolStripMenuItem
            // 
            文件ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 打开ToolStripMenuItem, 保存ToolStripMenuItem1, 导入ToolStripMenuItem, 导出ToolStripMenuItem, 设置ToolStripMenuItem });
            文件ToolStripMenuItem.Name = "文件ToolStripMenuItem";
            文件ToolStripMenuItem.Size = new Size(53, 24);
            文件ToolStripMenuItem.Text = "文件";
            // 
            // 打开ToolStripMenuItem
            // 
            打开ToolStripMenuItem.Name = "打开ToolStripMenuItem";
            打开ToolStripMenuItem.Size = new Size(224, 26);
            打开ToolStripMenuItem.Text = "打开";
            打开ToolStripMenuItem.Click += 打开ToolStripMenuItem_Click;
            // 
            // 保存ToolStripMenuItem1
            // 
            保存ToolStripMenuItem1.Name = "保存ToolStripMenuItem1";
            保存ToolStripMenuItem1.Size = new Size(224, 26);
            保存ToolStripMenuItem1.Text = "保存";
            保存ToolStripMenuItem1.Click += 保存ToolStripMenuItem1_Click;
            // 
            // 导入ToolStripMenuItem
            // 
            导入ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 节点定义表jsonToolStripMenuItem1 });
            导入ToolStripMenuItem.Name = "导入ToolStripMenuItem";
            导入ToolStripMenuItem.Size = new Size(224, 26);
            导入ToolStripMenuItem.Text = "导入";
            // 
            // 节点定义表jsonToolStripMenuItem1
            // 
            节点定义表jsonToolStripMenuItem1.Enabled = false;
            节点定义表jsonToolStripMenuItem1.Name = "节点定义表jsonToolStripMenuItem1";
            节点定义表jsonToolStripMenuItem1.Size = new Size(192, 26);
            节点定义表jsonToolStripMenuItem1.Text = "定义表 (*.json)";
            节点定义表jsonToolStripMenuItem1.Click += ImportNodeDefToolStripMenuItem_Click;
            // 
            // 导出ToolStripMenuItem
            // 
            导出ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 当前文件ToolStripMenuItem, 整个工作区pakToolStripMenuItem, 节点定义表jsonToolStripMenuItem });
            导出ToolStripMenuItem.Name = "导出ToolStripMenuItem";
            导出ToolStripMenuItem.Size = new Size(224, 26);
            导出ToolStripMenuItem.Text = "导出";
            // 
            // 当前文件ToolStripMenuItem
            // 
            当前文件ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { uAssetFileuassetToolStripMenuItem, uAssetJsonjsonToolStripMenuItem, blueprintDatajsonToolStripMenuItem });
            当前文件ToolStripMenuItem.Enabled = false;
            当前文件ToolStripMenuItem.Name = "当前文件ToolStripMenuItem";
            当前文件ToolStripMenuItem.Size = new Size(224, 26);
            当前文件ToolStripMenuItem.Text = "当前文件";
            // 
            // uAssetFileuassetToolStripMenuItem
            // 
            uAssetFileuassetToolStripMenuItem.Name = "uAssetFileuassetToolStripMenuItem";
            uAssetFileuassetToolStripMenuItem.Size = new Size(295, 26);
            uAssetFileuassetToolStripMenuItem.Text = "UAsset File (*.uasset)";
            uAssetFileuassetToolStripMenuItem.Click += uAssetFileuassetToolStripMenuItem_Click;
            // 
            // uAssetJsonjsonToolStripMenuItem
            // 
            uAssetJsonjsonToolStripMenuItem.Name = "uAssetJsonjsonToolStripMenuItem";
            uAssetJsonjsonToolStripMenuItem.Size = new Size(295, 26);
            uAssetJsonjsonToolStripMenuItem.Text = "UAssetAPI Json (*.json)";
            uAssetJsonjsonToolStripMenuItem.Click += uAssetJsonToolStripMenuItem_Click;
            // 
            // blueprintDatajsonToolStripMenuItem
            // 
            blueprintDatajsonToolStripMenuItem.Name = "blueprintDatajsonToolStripMenuItem";
            blueprintDatajsonToolStripMenuItem.Size = new Size(295, 26);
            blueprintDatajsonToolStripMenuItem.Text = "调试：Blueprint Data (*.json)";
            blueprintDatajsonToolStripMenuItem.Visible = false;
            blueprintDatajsonToolStripMenuItem.Click += blueprintDatajsonToolStripMenuItem_Click;
            // 
            // 整个工作区pakToolStripMenuItem
            // 
            整个工作区pakToolStripMenuItem.Enabled = false;
            整个工作区pakToolStripMenuItem.Name = "整个工作区pakToolStripMenuItem";
            整个工作区pakToolStripMenuItem.Size = new Size(224, 26);
            整个工作区pakToolStripMenuItem.Text = "整个工作区 (*.pak)";
            整个工作区pakToolStripMenuItem.Click += 整个工作区pakToolStripMenuItem_Click;
            // 
            // 节点定义表jsonToolStripMenuItem
            // 
            节点定义表jsonToolStripMenuItem.Name = "节点定义表jsonToolStripMenuItem";
            节点定义表jsonToolStripMenuItem.Size = new Size(224, 26);
            节点定义表jsonToolStripMenuItem.Text = "定义表 (*.json)";
            节点定义表jsonToolStripMenuItem.Click += ExportNodeDefToolStripMenuItem_Click;
            // 
            // 设置ToolStripMenuItem
            // 
            设置ToolStripMenuItem.Name = "设置ToolStripMenuItem";
            设置ToolStripMenuItem.Size = new Size(224, 26);
            设置ToolStripMenuItem.Text = "设置";
            设置ToolStripMenuItem.Click += 设置ToolStripMenuItem_Click;
            // 
            // 操作ToolStripMenuItem
            // 
            操作ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 扫描节点定义ToolStripMenuItem });
            操作ToolStripMenuItem.Name = "操作ToolStripMenuItem";
            操作ToolStripMenuItem.Size = new Size(53, 24);
            操作ToolStripMenuItem.Text = "操作";
            // 
            // 扫描节点定义ToolStripMenuItem
            // 
            扫描节点定义ToolStripMenuItem.Enabled = false;
            扫描节点定义ToolStripMenuItem.Name = "扫描节点定义ToolStripMenuItem";
            扫描节点定义ToolStripMenuItem.Size = new Size(224, 26);
            扫描节点定义ToolStripMenuItem.Text = "扫描定义";
            扫描节点定义ToolStripMenuItem.Click += 扫描节点定义ToolStripMenuItem_Click;
            // 
            // 帮助ToolStripMenuItem
            // 
            帮助ToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { 使用方法ToolStripMenuItem, 关于ToolStripMenuItem });
            帮助ToolStripMenuItem.Name = "帮助ToolStripMenuItem";
            帮助ToolStripMenuItem.Size = new Size(53, 24);
            帮助ToolStripMenuItem.Text = "帮助";
            // 
            // 使用方法ToolStripMenuItem
            // 
            使用方法ToolStripMenuItem.Name = "使用方法ToolStripMenuItem";
            使用方法ToolStripMenuItem.Size = new Size(152, 26);
            使用方法ToolStripMenuItem.Text = "使用方法";
            使用方法ToolStripMenuItem.Click += 使用方法ToolStripMenuItem_Click;
            // 
            // 关于ToolStripMenuItem
            // 
            关于ToolStripMenuItem.Name = "关于ToolStripMenuItem";
            关于ToolStripMenuItem.Size = new Size(152, 26);
            关于ToolStripMenuItem.Text = "关于";
            关于ToolStripMenuItem.Click += 关于ToolStripMenuItem_Click;
            // 
            // tableLayoutPanel1
            // 
            tableLayoutPanel1.ColumnCount = 3;
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
            tableLayoutPanel1.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60F));
            tableLayoutPanel1.Controls.Add(tableLayoutPanel3, 2, 0);
            tableLayoutPanel1.Controls.Add(tabControl2, 0, 0);
            tableLayoutPanel1.Controls.Add(tabControl3, 1, 0);
            tableLayoutPanel1.Dock = DockStyle.Fill;
            tableLayoutPanel1.Location = new Point(0, 28);
            tableLayoutPanel1.Name = "tableLayoutPanel1";
            tableLayoutPanel1.Padding = new Padding(5);
            tableLayoutPanel1.RowCount = 1;
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            tableLayoutPanel1.RowStyles.Add(new RowStyle(SizeType.Absolute, 20F));
            tableLayoutPanel1.Size = new Size(800, 412);
            tableLayoutPanel1.TabIndex = 2;
            // 
            // tableLayoutPanel3
            // 
            tableLayoutPanel3.ColumnCount = 1;
            tableLayoutPanel3.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel3.Controls.Add(tabControl1, 0, 0);
            tableLayoutPanel3.Controls.Add(label1, 0, 1);
            tableLayoutPanel3.Dock = DockStyle.Fill;
            tableLayoutPanel3.Location = new Point(324, 8);
            tableLayoutPanel3.Name = "tableLayoutPanel3";
            tableLayoutPanel3.RowCount = 2;
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 95F));
            tableLayoutPanel3.RowStyles.Add(new RowStyle(SizeType.Percent, 5F));
            tableLayoutPanel3.Size = new Size(468, 396);
            tableLayoutPanel3.TabIndex = 6;
            // 
            // tabControl1
            // 
            tabControl1.Dock = DockStyle.Fill;
            tabControl1.DrawMode = TabDrawMode.OwnerDrawFixed;
            tabControl1.Location = new Point(3, 3);
            tabControl1.Name = "tabControl1";
            tabControl1.SelectedIndex = 0;
            tabControl1.Size = new Size(462, 370);
            tabControl1.TabIndex = 5;
            tabControl1.SelectedIndexChanged += tabControl1_SelectedIndexChanged;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Dock = DockStyle.Fill;
            label1.Location = new Point(3, 376);
            label1.Name = "label1";
            label1.Size = new Size(462, 20);
            label1.TabIndex = 0;
            label1.Text = "正常";
            // 
            // tabControl2
            // 
            tabControl2.Controls.Add(tabPage1);
            tabControl2.Controls.Add(tabPage2);
            tabControl2.Dock = DockStyle.Fill;
            tabControl2.Location = new Point(8, 8);
            tabControl2.Name = "tabControl2";
            tabControl2.SelectedIndex = 0;
            tabControl2.Size = new Size(152, 396);
            tabControl2.TabIndex = 7;
            // 
            // tabPage1
            // 
            tabPage1.Controls.Add(treeView1);
            tabPage1.Location = new Point(4, 29);
            tabPage1.Name = "tabPage1";
            tabPage1.Padding = new Padding(3);
            tabPage1.Size = new Size(144, 363);
            tabPage1.TabIndex = 0;
            tabPage1.Text = "源文件";
            tabPage1.UseVisualStyleBackColor = true;
            // 
            // treeView1
            // 
            treeView1.BackColor = SystemColors.Window;
            treeView1.Dock = DockStyle.Fill;
            treeView1.HideSelection = false;
            treeView1.Location = new Point(3, 3);
            treeView1.Name = "treeView1";
            treeView1.Size = new Size(138, 357);
            treeView1.TabIndex = 0;
            treeView1.NodeMouseDoubleClick += treeView1_NodeMouseDoubleClick;
            treeView1.MouseDown += treeView1_MouseDown;
            // 
            // tabPage2
            // 
            tabPage2.Controls.Add(treeView2);
            tabPage2.Location = new Point(4, 29);
            tabPage2.Name = "tabPage2";
            tabPage2.Padding = new Padding(3);
            tabPage2.Size = new Size(144, 363);
            tabPage2.TabIndex = 1;
            tabPage2.Text = "工作区";
            tabPage2.UseVisualStyleBackColor = true;
            // 
            // treeView2
            // 
            treeView2.BackColor = SystemColors.Window;
            treeView2.Dock = DockStyle.Fill;
            treeView2.Location = new Point(3, 3);
            treeView2.Name = "treeView2";
            treeView2.Size = new Size(138, 357);
            treeView2.TabIndex = 0;
            treeView2.NodeMouseDoubleClick += treeView2_NodeMouseDoubleClickAsync;
            treeView2.MouseDown += treeView2_MouseDown;
            // 
            // tabControl3
            // 
            tabControl3.Controls.Add(tabPage3);
            tabControl3.Controls.Add(tabPage4);
            tabControl3.Dock = DockStyle.Fill;
            tabControl3.Location = new Point(166, 8);
            tabControl3.Name = "tabControl3";
            tabControl3.SelectedIndex = 0;
            tabControl3.Size = new Size(152, 396);
            tabControl3.TabIndex = 8;
            // 
            // tabPage3
            // 
            tabPage3.Controls.Add(tableLayoutPanel4);
            tabPage3.Location = new Point(4, 29);
            tabPage3.Name = "tabPage3";
            tabPage3.Padding = new Padding(3);
            tabPage3.Size = new Size(144, 363);
            tabPage3.TabIndex = 0;
            tabPage3.Text = "蓝图节点库";
            tabPage3.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel4
            // 
            tableLayoutPanel4.ColumnCount = 1;
            tableLayoutPanel4.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel4.Controls.Add(nodeLibrary, 0, 1);
            tableLayoutPanel4.Controls.Add(panel1, 0, 0);
            tableLayoutPanel4.Dock = DockStyle.Fill;
            tableLayoutPanel4.Location = new Point(3, 3);
            tableLayoutPanel4.Margin = new Padding(0);
            tableLayoutPanel4.Name = "tableLayoutPanel4";
            tableLayoutPanel4.RowCount = 2;
            tableLayoutPanel4.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            tableLayoutPanel4.RowStyles.Add(new RowStyle());
            tableLayoutPanel4.Size = new Size(138, 357);
            tableLayoutPanel4.TabIndex = 7;
            // 
            // nodeLibrary
            // 
            nodeLibrary.BackColor = SystemColors.Window;
            nodeLibrary.Dock = DockStyle.Fill;
            nodeLibrary.FormattingEnabled = true;
            nodeLibrary.Location = new Point(0, 35);
            nodeLibrary.Margin = new Padding(0);
            nodeLibrary.Name = "nodeLibrary";
            nodeLibrary.Size = new Size(138, 322);
            nodeLibrary.TabIndex = 1;
            // 
            // panel1
            // 
            panel1.Controls.Add(textBox1);
            panel1.Controls.Add(button1);
            panel1.Dock = DockStyle.Fill;
            panel1.Location = new Point(3, 3);
            panel1.Name = "panel1";
            panel1.Size = new Size(132, 29);
            panel1.TabIndex = 2;
            // 
            // textBox1
            // 
            textBox1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBox1.BackColor = SystemColors.Window;
            textBox1.Font = new Font("Microsoft YaHei UI", 9F);
            textBox1.Location = new Point(0, 0);
            textBox1.Margin = new Padding(0);
            textBox1.MaximumSize = new Size(999, 9999);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(103, 27);
            textBox1.TabIndex = 1;
            textBox1.KeyDown += textBox1_KeyDown;
            // 
            // button1
            // 
            button1.Dock = DockStyle.Right;
            button1.Font = new Font("Microsoft YaHei UI", 5F);
            button1.Location = new Point(103, 0);
            button1.Margin = new Padding(0);
            button1.Name = "button1";
            button1.Size = new Size(29, 29);
            button1.TabIndex = 0;
            button1.Text = "🔍";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // tabPage4
            // 
            tabPage4.Controls.Add(tableLayoutPanel2);
            tabPage4.Location = new Point(4, 29);
            tabPage4.Name = "tabPage4";
            tabPage4.Padding = new Padding(3);
            tabPage4.Size = new Size(144, 363);
            tabPage4.TabIndex = 1;
            tabPage4.Text = "非蓝图属性库";
            tabPage4.UseVisualStyleBackColor = true;
            // 
            // tableLayoutPanel2
            // 
            tableLayoutPanel2.ColumnCount = 1;
            tableLayoutPanel2.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
            tableLayoutPanel2.Controls.Add(panel2, 0, 0);
            tableLayoutPanel2.Controls.Add(dataLibrary, 0, 1);
            tableLayoutPanel2.Dock = DockStyle.Fill;
            tableLayoutPanel2.Location = new Point(3, 3);
            tableLayoutPanel2.Margin = new Padding(0);
            tableLayoutPanel2.Name = "tableLayoutPanel2";
            tableLayoutPanel2.RowCount = 2;
            tableLayoutPanel2.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
            tableLayoutPanel2.RowStyles.Add(new RowStyle());
            tableLayoutPanel2.Size = new Size(138, 357);
            tableLayoutPanel2.TabIndex = 7;
            // 
            // panel2
            // 
            panel2.Controls.Add(textBox2);
            panel2.Controls.Add(button2);
            panel2.Dock = DockStyle.Fill;
            panel2.Location = new Point(3, 3);
            panel2.Name = "panel2";
            panel2.Size = new Size(132, 29);
            panel2.TabIndex = 2;
            // 
            // textBox2
            // 
            textBox2.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            textBox2.BackColor = SystemColors.Window;
            textBox2.Font = new Font("Microsoft YaHei UI", 9F);
            textBox2.Location = new Point(0, 0);
            textBox2.Margin = new Padding(0);
            textBox2.MaximumSize = new Size(999, 9999);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(103, 27);
            textBox2.TabIndex = 1;
            // 
            // button2
            // 
            button2.Dock = DockStyle.Right;
            button2.Font = new Font("Microsoft YaHei UI", 5F);
            button2.Location = new Point(103, 0);
            button2.Margin = new Padding(0);
            button2.Name = "button2";
            button2.Size = new Size(29, 29);
            button2.TabIndex = 0;
            button2.Text = "🔍";
            button2.UseVisualStyleBackColor = true;
            // 
            // dataLibrary
            // 
            dataLibrary.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            dataLibrary.BackgroundColor = SystemColors.Window;
            dataLibrary.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.AutoSize;
            dataLibrary.Dock = DockStyle.Fill;
            dataLibrary.Location = new Point(3, 38);
            dataLibrary.Name = "dataLibrary";
            dataLibrary.RowHeadersVisible = false;
            dataLibrary.RowHeadersWidth = 51;
            dataLibrary.Size = new Size(132, 316);
            dataLibrary.TabIndex = 3;
            // 
            // Form1
            // 
            AutoScaleMode = AutoScaleMode.Inherit;
            ClientSize = new Size(800, 440);
            Controls.Add(tableLayoutPanel1);
            Controls.Add(menuStrip1);
            KeyPreview = true;
            MainMenuStrip = menuStrip1;
            MinimumSize = new Size(818, 487);
            Name = "Form1";
            Text = "BPEditor";
            WindowState = FormWindowState.Maximized;
            KeyDown += Form1_KeyDown;
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            tableLayoutPanel1.ResumeLayout(false);
            tableLayoutPanel3.ResumeLayout(false);
            tableLayoutPanel3.PerformLayout();
            tabControl2.ResumeLayout(false);
            tabPage1.ResumeLayout(false);
            tabPage2.ResumeLayout(false);
            tabControl3.ResumeLayout(false);
            tabPage3.ResumeLayout(false);
            tableLayoutPanel4.ResumeLayout(false);
            panel1.ResumeLayout(false);
            panel1.PerformLayout();
            tabPage4.ResumeLayout(false);
            tableLayoutPanel2.ResumeLayout(false);
            panel2.ResumeLayout(false);
            panel2.PerformLayout();
            ((System.ComponentModel.ISupportInitialize)dataLibrary).EndInit();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip1;
        private ToolStripMenuItem 文件ToolStripMenuItem;
        private ToolStripMenuItem 打开ToolStripMenuItem;
        private ToolStripMenuItem 设置ToolStripMenuItem;
        private TableLayoutPanel tableLayoutPanel1;
        private TableLayoutPanel tableLayoutPanel3;
        private Label label1;
        private CloseableTabControl tabControl1;
        private ToolStripMenuItem 导出ToolStripMenuItem;
        private TabControl tabControl2;
        private TabPage tabPage1;
        private TreeView treeView1;
        private TabPage tabPage2;
        private TreeView treeView2;
        private ToolStripMenuItem 导入ToolStripMenuItem;
        private ToolStripMenuItem 当前文件ToolStripMenuItem;
        private ToolStripMenuItem uAssetFileuassetToolStripMenuItem;
        private ToolStripMenuItem uAssetJsonjsonToolStripMenuItem;
        private ToolStripMenuItem 整个工作区pakToolStripMenuItem;
        private ToolStripMenuItem 节点定义表jsonToolStripMenuItem;
        private ToolStripMenuItem 节点定义表jsonToolStripMenuItem1;
        private ToolStripMenuItem 操作ToolStripMenuItem;
        private ToolStripMenuItem 扫描节点定义ToolStripMenuItem;
        private ToolStripMenuItem 保存ToolStripMenuItem1;
        private ToolStripMenuItem 帮助ToolStripMenuItem;
        private ToolStripMenuItem 使用方法ToolStripMenuItem;
        private ToolStripMenuItem 关于ToolStripMenuItem;
        public ToolStripMenuItem blueprintDatajsonToolStripMenuItem;
        private TabControl tabControl3;
        private TabPage tabPage3;
        private TableLayoutPanel tableLayoutPanel4;
        private ListBox nodeLibrary;
        private Panel panel1;
        private TextBox textBox1;
        private Button button1;
        private TabPage tabPage4;
        private TableLayoutPanel tableLayoutPanel2;
        private Panel panel2;
        private TextBox textBox2;
        private Button button2;
        private DataGridView dataLibrary;
    }
}
