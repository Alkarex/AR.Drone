using AR.Drone.Client;
using AR.Drone.Client.Commands;
using AR.Drone.Client.Configuration;
using AR.Drone.Data;
using AR.Drone.Data.Navigation;
using AR.Drone.Data.Navigation.Native;
using AR.Drone.Media;
using AR.Drone.Video;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace AR.Drone.WinApp
{
    public partial class MainForm : Form
    {
        private readonly DroneClient _droneClient;
        private readonly List<PlayerForm> _playerForms;
        private readonly VideoPacketDecoderWorker _videoPacketDecoderWorker;
        private DroneConfiguration _configuration;
        private VideoFrame _frame;
        private Bitmap _frameBitmap;
        private uint _frameNumber;
        private NavigationData _navigationData;
        private NavigationPacket _navigationPacket;
        private PacketRecorder _packetRecorderWorker;
        private FileStream _recorderStream;

        public MainForm()
        {
            InitializeComponent();

            _videoPacketDecoderWorker = new VideoPacketDecoderWorker(PixelFormat.BGR24, true, OnVideoPacketDecoded);
            _videoPacketDecoderWorker.Start();

            _droneClient = new DroneClient();
            _droneClient.NavigationPacketAcquired += OnNavigationPacketAcquired;
            _droneClient.VideoPacketAcquired += OnVideoPacketAcquired;
            _droneClient.NavigationDataAcquired += data => _navigationData = data;
			//_droneClient.OnProgress += _droneClient_OnProgress;

            tmrStateUpdate.Enabled = true;
            tmrVideoUpdate.Enabled = true;

            _playerForms = new List<PlayerForm>();

            _videoPacketDecoderWorker.UnhandledException += UnhandledException;
        }

        private void UnhandledException(object sender, Exception exception)
        {
            MessageBox.Show(exception.ToString(), "Unhandled Exception (Ctrl+C)", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            Text += Environment.Is64BitProcess ? " [64-bit]" : " [32-bit]";
        }

        protected override void OnClosed(EventArgs e)
        {
			UserInputLogClose();	//Alex
            StopRecording();

            _droneClient.Dispose();
            _videoPacketDecoderWorker.Dispose();

            base.OnClosed(e);
        }

        private void OnNavigationPacketAcquired(NavigationPacket packet)
        {
            if (_packetRecorderWorker != null && _packetRecorderWorker.IsAlive)
                _packetRecorderWorker.EnqueuePacket(packet);

            _navigationPacket = packet;
        }

        private void OnVideoPacketAcquired(VideoPacket packet)
        {
            if (_packetRecorderWorker != null && _packetRecorderWorker.IsAlive)
                _packetRecorderWorker.EnqueuePacket(packet);
            if (_videoPacketDecoderWorker.IsAlive)
                _videoPacketDecoderWorker.EnqueuePacket(packet);
        }

        private void OnVideoPacketDecoded(VideoFrame frame)
        {
            _frame = frame;
        }

        private void btnStart_Click(object sender, EventArgs e)
        {
            _droneClient.Start();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            _droneClient.Stop();
        }

        private void tmrVideoUpdate_Tick(object sender, EventArgs e)
        {
            if (_frame == null || _frameNumber == _frame.Number)
                return;
            _frameNumber = _frame.Number;

            if (_frameBitmap == null)
                _frameBitmap = VideoHelper.CreateBitmap(ref _frame);
            else
                VideoHelper.UpdateBitmap(ref _frameBitmap, ref _frame);

			DrawCross(ref _frameBitmap);	//Alex

            pbVideo.Image = _frameBitmap;
        }

		void DrawCross(ref Bitmap bitmap)
		{
			var cx = 4 + bitmap.Width / 2;
			var cy = 4 + bitmap.Height / 2;
			for (var x = cx - 8; x <= cx + 8; x++)
				//for (var y = cy - 1; y <= cy + 1; y++)
					bitmap.SetPixel(x, cy, Color.Lime);
			for (var y = cy - 8; y <= cy + 8; y++)
				//for (var x = cx - 1; x <= cx + 1; x++)
					bitmap.SetPixel(cx, y, Color.Lime);
		}

        private void tmrStateUpdate_Tick(object sender, EventArgs e)
        {
            tvInfo.BeginUpdate();

            TreeNode node = tvInfo.Nodes.GetOrCreate("ClientActive");
            node.Text = string.Format("Client Active: {0}", _droneClient.IsActive);

            node = tvInfo.Nodes.GetOrCreate("Navigation Data");
            if (_navigationData != null) DumpBranch(node.Nodes, _navigationData);

            node = tvInfo.Nodes.GetOrCreate("Configuration");
            if (_configuration != null) DumpBranch(node.Nodes, _configuration);

            TreeNode vativeNode = tvInfo.Nodes.GetOrCreate("Native");

            NavdataBag navdataBag;
            if (_navigationPacket.Data != null && NavdataBagParser.TryParse(ref _navigationPacket, out navdataBag))
            {
                var ctrl_state = (CTRL_STATES) (navdataBag.demo.ctrl_state >> 0x10);
                node = vativeNode.Nodes.GetOrCreate("ctrl_state");
                node.Text = string.Format("Ctrl State: {0}", ctrl_state);

                var flying_state = (FLYING_STATES) (navdataBag.demo.ctrl_state & 0xffff);
                node = vativeNode.Nodes.GetOrCreate("flying_state");
                node.Text = string.Format("Ctrl State: {0}", flying_state);

                DumpBranch(vativeNode.Nodes, navdataBag);

				labelLowBattery.Visible = _navigationData.Battery.Low;
            }
            tvInfo.EndUpdate();
        }

        private void DumpBranch(TreeNodeCollection nodes, object o)
        {
            Type type = o.GetType();
            FieldInfo[] fields = type.GetFields();
            foreach (FieldInfo fieldInfo in fields)
            {
                TreeNode node = nodes.GetOrCreate(fieldInfo.Name);
                object fieldValue = fieldInfo.GetValue(o);

                if (fieldValue == null)
                    node.Text = node.Name + ": null";
                else if (fieldValue is IConfigurationItem)
                    node.Text = node.Name + ": " + ((IConfigurationItem) fieldValue).Value;
                else
                {
                    Type fieldType = fieldInfo.FieldType;
                    if (fieldType.Namespace.StartsWith("System") || fieldType.IsEnum)
                        node.Text = node.Name + ": " + fieldValue;
                    else
                        DumpBranch(node.Nodes, fieldValue);
                }
            }
        }

        private void btnFlatTrim_Click(object sender, EventArgs e)
        {
            _droneClient.FlatTrim();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            _droneClient.Takeoff();
        }

        private void button3_Click(object sender, EventArgs e)
        {
            _droneClient.Land();
        }

        private void btnEmergency_Click(object sender, EventArgs e)
        {
            _droneClient.Emergency();
        }

        private void btnReset_Click(object sender, EventArgs e)
        {
            _droneClient.ResetEmergency();
        }

        private void btnSwitchCam_Click(object sender, EventArgs e)
        {
            DroneConfiguration configuration = _configuration ?? new DroneConfiguration();
            configuration.Video.Channel.ChangeTo(VideoChannelType.Next);
            configuration.SendTo(_droneClient);
        }

        private void btnHover_Click(object sender, EventArgs e)
        {
			checkBoxMouseEnabled.Checked = false;
			checkBoxMouseEnabled_CheckedChanged(sender, e);
            _droneClient.Hover();
        }

        private void btnUp_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, gaz: 0.25f);
        }

        private void btnDown_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, gaz: -0.25f);
        }

        private void btnTurnLeft_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, yaw: 0.25f);
        }

        private void btnTurnRight_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, yaw: -0.25f);
        }

        private void btnLeft_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, roll: -0.05f);
        }

        private void btnRight_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, roll: 0.05f);
        }

        private void btnForward_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, pitch: -0.05f);
        }

        private void btnBack_Click(object sender, EventArgs e)
        {
            _droneClient.Progress(FlightMode.Progressive, pitch: 0.05f);
        }

        private void btnReadConfig_Click(object sender, EventArgs e)
        {
            Task<DroneConfiguration> configurationTask = _droneClient.GetConfigurationTask();
            configurationTask.ContinueWith(delegate(Task<DroneConfiguration> task)
                {
                    if (task.Exception != null)
                    {
                        Trace.TraceWarning("Get configuration task is faulted with exception: {0}", task.Exception.InnerException.Message);
                        return;
                    }

                    _configuration = task.Result;
                });
            configurationTask.Start();
        }

        private void btnSendConfig_Click(object sender, EventArgs e)
        {
            DroneConfiguration configuration = _configuration ?? new DroneConfiguration();

            configuration.Video.BitrateCtrlMode.ChangeTo(VideoBitrateControlMode.Manual);
            //configuration.Video.Codec.ChangeTo(VideoCodecType.H264_720P);
            //configuration.Video.MaxBitrate.ChangeTo(1100);

            // send all changes in one pice
            configuration.SendTo(_droneClient);
        }

        private void StopRecording()
        {
            if (_packetRecorderWorker != null)
            {
                _packetRecorderWorker.Stop();
                _packetRecorderWorker.Join();
                _packetRecorderWorker = null;
            }
            if (_recorderStream != null)
            {
                _recorderStream.Dispose();
                _recorderStream = null;
            }
        }

        private void btnStartRecording_Click(object sender, EventArgs e)
        {
            string path = string.Format("flight_{0:yyyy_MM_dd_HH_mm}.ardrone", DateTime.Now);

            using (var dialog = new SaveFileDialog {DefaultExt = ".ardrone", Filter = "AR.Drone track files (*.ardrone)|*.ardrone", FileName = path})
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StopRecording();

                    _recorderStream = new FileStream(dialog.FileName, FileMode.OpenOrCreate);
                    _packetRecorderWorker = new PacketRecorder(_recorderStream);
                    _packetRecorderWorker.Start();
                }
            }
        }

        private void btnStopRecording_Click(object sender, EventArgs e)
        {
            StopRecording();
        }

        private void btnReplay_Click(object sender, EventArgs e)
        {
            using (var dialog = new OpenFileDialog {DefaultExt = ".ardrone", Filter = "AR.Drone track files (*.ardrone)|*.ardrone"})
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    StopRecording();

                    var playerForm = new PlayerForm {FileName = dialog.FileName};
                    playerForm.Closed += (o, args) => _playerForms.Remove(o as PlayerForm);
                    _playerForms.Add(playerForm);
                    playerForm.Show(this);
                }
            }
        }

		TextWriter userInputLog;	//Alex

		void UserInputLogOpen()
		{
			if (userInputLog == null)
			{
				/*var path = String.Format(CultureInfo.InvariantCulture, "UserInputLog-{0}.txt.gzip", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH'-'mm'-'ss"));
					var fileStream = File.Create(path);
					var gzip = new GZipStream(fileStream, CompressionMode.Compress);
					userInputLog = new StreamWriter(gzip, Encoding.UTF8);*/
				if (!Directory.Exists("../../../logs/"))
					Directory.CreateDirectory("../../../logs/");
				var path = String.Format(CultureInfo.InvariantCulture, "../../../logs/UserInputLog-{0}.txt", DateTime.Now.ToString("yyyy'-'MM'-'dd'T'HH'-'mm'-'ss"));
				userInputLog = new StreamWriter(path, true, Encoding.UTF8);
				//userInputLog.WriteLine("{0}\tInit\t{1}\t{2}", DateTime.Now.Ticks, MouseInput.GazeModeX, MouseInput.GazeModeY);
			}
		}

		void UserInputLog(String line)	//Alex
		{
			if (userInputLog != null)
				userInputLog.WriteLine(line);
		}

		void UserInputLogClose()	//Alex
		{
			if (userInputLog != null)
			{
				userInputLog.Close();
				userInputLog = null;
			}
		}

		//readonly double PrimaryScreenWidthCentre = SystemParameters.PrimaryScreenWidth / 2.0;	//Alex
		//readonly double PrimaryScreenHeightCentre = SystemParameters.PrimaryScreenHeight / 2.0;	//Alex

		static float MapOrderRange(double raw, double orderAttenuation, double minOrderValue, double maxOrderValue)
		{//Alex
			if (Math.Abs(raw) < 0.02) return 0.0f;
			var result = orderAttenuation * raw;
			if (result > maxOrderValue) return (float)maxOrderValue;
			if (result < minOrderValue) return (float)minOrderValue;
			return (float)result;
		}

		void ProcessPrepare(double roll, double pitch, double yaw, double gaz)
		{
			//labelDebug.Top = pbVideo.Height / 2;
			//labelDebug.Left = pbVideo.Width / 2;
			labelDebug.Text = MapOrderRange(roll, orderAttenuation: 0.3, minOrderValue: -0.1, maxOrderValue: 0.1).ToString() + '|' +
				MapOrderRange(pitch, orderAttenuation: 0.3, minOrderValue: -0.1, maxOrderValue: 0.1).ToString() + '|' +
				MapOrderRange(yaw, orderAttenuation: 0.5, minOrderValue: -0.2, maxOrderValue: 0.2).ToString() + '|' +
				MapOrderRange(gaz, orderAttenuation: 0.5, minOrderValue: -0.5, maxOrderValue: 0.5).ToString();

			_droneClient.Progress(FlightMode.Progressive,
				MapOrderRange(roll, orderAttenuation: 0.3, minOrderValue: -0.1, maxOrderValue: 0.1),
				MapOrderRange(pitch, orderAttenuation: 0.3, minOrderValue: -0.1, maxOrderValue: 0.1),
				MapOrderRange(yaw, orderAttenuation: 0.5, minOrderValue: -0.2, maxOrderValue: 0.2),
				MapOrderRange(gaz, orderAttenuation: 0.5, minOrderValue: -0.5, maxOrderValue: 0.5));
		}

		void timerInputControls_Tick(object sender, EventArgs e)	//Alex
		{
			var xCentre = pbVideo.Width / 2.0;
			var yCentre = pbVideo.Height / 2.0;
			var xValue = (Control.MousePosition.X - xCentre) / xCentre;
			var yValue = (Control.MousePosition.Y - yCentre) / yCentre;
			//labelDebug.Text = xValue.ToString() + '/' + yValue.ToString();
			switch (comboBoxMouseMode.SelectedIndex)
			{
				case 0:	//1.XTranslation/YSpeed
					ProcessPrepare(roll: xValue,	pitch: yValue,		yaw: wKeyvalue,	gaz: zKeyvalue);
					break;
				case 1:	//2.XRotation/YSpeed
					ProcessPrepare(roll: wKeyvalue,	pitch: yValue,		yaw: xValue,	gaz: zKeyvalue);
					break;
				case 2:	//3.XTranslation/YAltitude
					ProcessPrepare(roll: xValue,	pitch: -zKeyvalue,	yaw: wKeyvalue,	gaz: -yValue);
					break;
				case 3:	//4.XRotation/YAltitude
					ProcessPrepare(roll: wKeyvalue,	pitch: -zKeyvalue,	yaw: xValue,	gaz: -yValue);
					break;
				default:
					break;
			}
			UserInputLog(String.Format("{0}\t{1}\t{2}\t{3}", DateTime.Now.Ticks, comboBoxMouseMode.Text, xValue, yValue));
		}

		private void checkBoxMouseEnabled_CheckedChanged(object sender, EventArgs e)
		{
			if (checkBoxMouseEnabled.Checked)
			{
				checkBoxMouseEnabled.Focus();
				timerInputControls.Enabled = true;
			}
			else
			{
				timerInputControls.Enabled = false;
				_droneClient.Hover();
			}
		}

		double zKeyvalue = 0.0;
		double wKeyvalue = 0.0;

		private void MainForm_KeyDown(object sender, KeyEventArgs e)
		{
			var keyCode = e.KeyCode;
			switch (e.KeyValue)
			{
				case 16:
					keyCode = Keys.LShiftKey;
					break;
				case 17:
					keyCode = Keys.LControlKey;
					break;
			}
			var prevent = true;
			switch (keyCode)
			{
				case Keys.T:
				case Keys.LShiftKey:
				case Keys.F11:
					if (checkBoxMouseEnabled.Enabled)
					{
						UserInputLogOpen();
						_droneClient.Takeoff();
						base.WindowState = FormWindowState.Maximized;
						base.FormBorderStyle = FormBorderStyle.None;
						pbVideo.Dock = DockStyle.Fill;
						Thread.Sleep(500);
						_droneClient.Hover();
					}
					break;
				case Keys.G:
				case Keys.M:
				case Keys.LControlKey:
					if (checkBoxMouseEnabled.Enabled)
					{
						checkBoxMouseEnabled.Checked = !checkBoxMouseEnabled.Checked;
						checkBoxMouseEnabled_CheckedChanged(sender, e);
					}
					break;
				case Keys.Space:
				case Keys.Escape:
					timerInputControls.Enabled = false;
					checkBoxMouseEnabled.Checked = false;
					base.FormBorderStyle = FormBorderStyle.Sizable;
					pbVideo.Dock = DockStyle.None;
					Thread.Sleep(100);
					_droneClient.Land();
					UserInputLogClose();
					break;
				case Keys.NumPad8:
				case Keys.Up:
					zKeyvalue = 0.75;
					break;
				case Keys.NumPad2:
				case Keys.Down:
					zKeyvalue = -0.75;
					break;
				case Keys.NumPad4:
				case Keys.Left:
					wKeyvalue = -1.0;
					break;
				case Keys.NumPad6:
				case Keys.Right:
					wKeyvalue = 1.0;
					break;
				default:
					prevent = false;
					break;
			}
			if (prevent)
				e.SuppressKeyPress = true;
			UserInputLog(String.Format("{0}\t{1}\t{2}\t{3}", DateTime.Now.Ticks, comboBoxMouseMode.Text, "KeyDown", keyCode));
		}

		private void MainForm_KeyUp(object sender, KeyEventArgs e)
		{
			var keyCode = e.KeyCode;
			switch (e.KeyValue)
			{
				case 16:
					keyCode = Keys.LShiftKey;
					break;
				case 17:
					keyCode = Keys.LControlKey;
					break;
			}
			switch (keyCode)
			{
				case Keys.NumPad8:
				case Keys.NumPad2:
				case Keys.Up:
				case Keys.Down:
					zKeyvalue = 0.0;
					break;
				case Keys.NumPad4:
				case Keys.NumPad6:
				case Keys.Left:
				case Keys.Right:
					wKeyvalue = 0.0;
					break;
			}
			UserInputLog(String.Format("{0}\t{1}\t{2}\t{3}", DateTime.Now.Ticks, comboBoxMouseMode.Text, "KeyUp", keyCode));
		}

		void comboBoxMouseMode_SelectedIndexChanged(object sender, EventArgs e)
		{
			checkBoxMouseEnabled.Enabled = true;
			checkBoxMouseEnabled.Checked = false;
			checkBoxMouseEnabled_CheckedChanged(sender, e);
		}
	}
}
