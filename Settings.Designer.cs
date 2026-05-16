namespace BPEditor
{
    partial class Settings
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
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
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            textBox1 = new TextBox();
            button1 = new Button();
            button2 = new Button();
            textBox2 = new TextBox();
            label2 = new Label();
            button3 = new Button();
            label3 = new Label();
            comboBox1 = new ComboBox();
            button4 = new Button();
            textBox3 = new TextBox();
            label4 = new Label();
            checkBox1 = new CheckBox();
            comboBox2 = new ComboBox();
            label5 = new Label();
            comboBox3 = new ComboBox();
            label6 = new Label();
            label7 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(28, 23);
            label1.Name = "label1";
            label1.Size = new Size(80, 20);
            label1.TabIndex = 0;
            label1.Text = "Pak Key：";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(143, 20);
            textBox1.Name = "textBox1";
            textBox1.Size = new Size(623, 27);
            textBox1.TabIndex = 1;
            // 
            // button1
            // 
            button1.Location = new Point(561, 311);
            button1.Name = "button1";
            button1.Size = new Size(94, 29);
            button1.TabIndex = 2;
            button1.Text = "确认";
            button1.UseVisualStyleBackColor = true;
            button1.Click += button1_Click;
            // 
            // button2
            // 
            button2.Location = new Point(672, 311);
            button2.Name = "button2";
            button2.Size = new Size(94, 29);
            button2.TabIndex = 3;
            button2.Text = "取消";
            button2.UseVisualStyleBackColor = true;
            // 
            // textBox2
            // 
            textBox2.Location = new Point(143, 53);
            textBox2.Name = "textBox2";
            textBox2.Size = new Size(582, 27);
            textBox2.TabIndex = 5;
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.Location = new Point(28, 56);
            label2.Name = "label2";
            label2.Size = new Size(84, 20);
            label2.TabIndex = 4;
            label2.Text = "工作目录：";
            // 
            // button3
            // 
            button3.Location = new Point(731, 52);
            button3.Name = "button3";
            button3.Size = new Size(35, 29);
            button3.TabIndex = 6;
            button3.Text = "...";
            button3.UseVisualStyleBackColor = true;
            button3.Click += button3_Click;
            // 
            // label3
            // 
            label3.AutoSize = true;
            label3.Location = new Point(28, 89);
            label3.Name = "label3";
            label3.Size = new Size(73, 20);
            label3.TabIndex = 7;
            label3.Text = "UE版本：";
            // 
            // comboBox1
            // 
            comboBox1.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox1.FormattingEnabled = true;
            comboBox1.Location = new Point(143, 86);
            comboBox1.Name = "comboBox1";
            comboBox1.Size = new Size(623, 28);
            comboBox1.TabIndex = 8;
            // 
            // button4
            // 
            button4.Location = new Point(731, 119);
            button4.Name = "button4";
            button4.Size = new Size(35, 29);
            button4.TabIndex = 11;
            button4.Text = "...";
            button4.UseVisualStyleBackColor = true;
            button4.Click += button4_Click;
            // 
            // textBox3
            // 
            textBox3.Location = new Point(143, 120);
            textBox3.Name = "textBox3";
            textBox3.Size = new Size(582, 27);
            textBox3.TabIndex = 10;
            // 
            // label4
            // 
            label4.AutoSize = true;
            label4.Location = new Point(28, 123);
            label4.Name = "label4";
            label4.Size = new Size(84, 20);
            label4.TabIndex = 9;
            label4.Text = "映射文件：";
            // 
            // checkBox1
            // 
            checkBox1.AutoSize = true;
            checkBox1.CheckAlign = ContentAlignment.MiddleRight;
            checkBox1.Location = new Point(28, 269);
            checkBox1.Name = "checkBox1";
            checkBox1.Size = new Size(251, 24);
            checkBox1.TabIndex = 12;
            checkBox1.Text = "调试：每次打开Pak时不重新解压";
            checkBox1.UseVisualStyleBackColor = true;
            checkBox1.Visible = false;
            // 
            // comboBox2
            // 
            comboBox2.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox2.FormattingEnabled = true;
            comboBox2.Location = new Point(143, 175);
            comboBox2.Name = "comboBox2";
            comboBox2.Size = new Size(623, 28);
            comboBox2.TabIndex = 14;
            // 
            // label5
            // 
            label5.AutoSize = true;
            label5.Location = new Point(28, 178);
            label5.Name = "label5";
            label5.Size = new Size(103, 20);
            label5.TabIndex = 13;
            label5.Text = "导出UE版本：";
            // 
            // comboBox3
            // 
            comboBox3.DropDownStyle = ComboBoxStyle.DropDownList;
            comboBox3.FormattingEnabled = true;
            comboBox3.Location = new Point(143, 209);
            comboBox3.Name = "comboBox3";
            comboBox3.Size = new Size(623, 28);
            comboBox3.TabIndex = 16;
            // 
            // label6
            // 
            label6.AutoSize = true;
            label6.Location = new Point(28, 212);
            label6.Name = "label6";
            label6.Size = new Size(109, 20);
            label6.TabIndex = 15;
            label6.Text = "导出Pak版本：";
            // 
            // label7
            // 
            label7.AutoSize = true;
            label7.Location = new Point(30, 315);
            label7.Name = "label7";
            label7.Size = new Size(71, 20);
            label7.TabIndex = 17;
            label7.Text = "BPEditor";
            label7.Click += label7_Click;
            // 
            // Settings
            // 
            AutoScaleDimensions = new SizeF(9F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            CancelButton = button2;
            ClientSize = new Size(802, 362);
            Controls.Add(label7);
            Controls.Add(comboBox3);
            Controls.Add(label6);
            Controls.Add(comboBox2);
            Controls.Add(label5);
            Controls.Add(checkBox1);
            Controls.Add(button4);
            Controls.Add(textBox3);
            Controls.Add(label4);
            Controls.Add(comboBox1);
            Controls.Add(label3);
            Controls.Add(button3);
            Controls.Add(textBox2);
            Controls.Add(label2);
            Controls.Add(button2);
            Controls.Add(button1);
            Controls.Add(textBox1);
            Controls.Add(label1);
            MaximizeBox = false;
            MaximumSize = new Size(820, 409);
            MinimizeBox = false;
            MinimumSize = new Size(820, 409);
            Name = "Settings";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "设置";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox textBox1;
        private Button button1;
        private Button button2;
        private TextBox textBox2;
        private Label label2;
        private Button button3;
        private Label label3;
        private ComboBox comboBox1;
        private Button button4;
        private TextBox textBox3;
        private Label label4;
        private CheckBox checkBox1;
        private ComboBox comboBox2;
        private Label label5;
        private ComboBox comboBox3;
        private Label label6;
        private Label label7;
    }
}