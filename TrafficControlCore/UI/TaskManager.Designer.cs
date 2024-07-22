namespace TrafficControlCore.UI
{
    partial class TaskManager
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
            this.components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(TaskManager));
            this.executingTaskslistView = new System.Windows.Forms.ListView();
            this.statusStrip1 = new System.Windows.Forms.StatusStrip();
            this.toolStripDropDownButton1 = new System.Windows.Forms.ToolStripDropDownButton();
            this.taskNum = new System.Windows.Forms.ToolStripStatusLabel();
            this.timer1 = new System.Windows.Forms.Timer(this.components);
            this.statusStrip1.SuspendLayout();
            this.SuspendLayout();
            // 
            // executingTaskslistView
            // 
            this.executingTaskslistView.BackColor = System.Drawing.SystemColors.Info;
            this.executingTaskslistView.Cursor = System.Windows.Forms.Cursors.Default;
            this.executingTaskslistView.Dock = System.Windows.Forms.DockStyle.Fill;
            this.executingTaskslistView.FullRowSelect = true;
            this.executingTaskslistView.GridLines = true;
            this.executingTaskslistView.HideSelection = false;
            this.executingTaskslistView.Location = new System.Drawing.Point(0, 0);
            this.executingTaskslistView.Margin = new System.Windows.Forms.Padding(5);
            this.executingTaskslistView.Name = "executingTaskslistView";
            this.executingTaskslistView.Size = new System.Drawing.Size(910, 441);
            this.executingTaskslistView.TabIndex = 44;
            this.executingTaskslistView.UseCompatibleStateImageBehavior = false;
            this.executingTaskslistView.View = System.Windows.Forms.View.Details;
            // 
            // statusStrip1
            // 
            this.statusStrip1.Items.AddRange(new System.Windows.Forms.ToolStripItem[] {
            this.toolStripDropDownButton1,
            this.taskNum});
            this.statusStrip1.Location = new System.Drawing.Point(0, 441);
            this.statusStrip1.Name = "statusStrip1";
            this.statusStrip1.Size = new System.Drawing.Size(910, 23);
            this.statusStrip1.TabIndex = 2;
            this.statusStrip1.Text = "statusStrip1";
            // 
            // toolStripDropDownButton1
            // 
            this.toolStripDropDownButton1.DisplayStyle = System.Windows.Forms.ToolStripItemDisplayStyle.Text;
            this.toolStripDropDownButton1.Image = ((System.Drawing.Image)(resources.GetObject("toolStripDropDownButton1.Image")));
            this.toolStripDropDownButton1.ImageTransparentColor = System.Drawing.Color.Magenta;
            this.toolStripDropDownButton1.Name = "toolStripDropDownButton1";
            this.toolStripDropDownButton1.Size = new System.Drawing.Size(45, 21);
            this.toolStripDropDownButton1.Text = "刷新";
            this.toolStripDropDownButton1.Click += new System.EventHandler(this.toolStripDropDownButton1_Click);
            // 
            // taskNum
            // 
            this.taskNum.Name = "taskNum";
            this.taskNum.Size = new System.Drawing.Size(131, 18);
            this.taskNum.Text = "toolStripStatusLabel1";
            // 
            // timer1
            // 
            this.timer1.Enabled = true;
            this.timer1.Interval = 3000;
            this.timer1.Tick += new System.EventHandler(this.timer1_Tick);
            // 
            // TaskManager
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(6F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(910, 464);
            this.Controls.Add(this.executingTaskslistView);
            this.Controls.Add(this.statusStrip1);
            this.MaximizeBox = false;
            this.Name = "TaskManager";
            this.Text = "TaskManager";
            this.statusStrip1.ResumeLayout(false);
            this.statusStrip1.PerformLayout();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion
        private System.Windows.Forms.StatusStrip statusStrip1;
        private System.Windows.Forms.ToolStripStatusLabel taskNum;
        private System.Windows.Forms.Timer timer1;
        private System.Windows.Forms.ListView executingTaskslistView;
        private System.Windows.Forms.ToolStripDropDownButton toolStripDropDownButton1;
    }
}