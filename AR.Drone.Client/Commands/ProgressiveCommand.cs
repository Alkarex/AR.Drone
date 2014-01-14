using AR.Drone.Data.Helpers;

namespace AR.Drone.Client.Commands
{
    public class ProgressCommand : ATCommand
    {
        public readonly FlightMode _mode;
		public readonly float _roll;
		public readonly float _pitch;
		public readonly float _yaw;
		public readonly float _gaz;

        public ProgressCommand(FlightMode mode, float roll, float pitch, float yaw, float gaz)
        {
            _mode = mode;
            _roll = roll;
            _pitch = pitch;
            _yaw = yaw;
            _gaz = gaz;
        }

        protected override string ToAt(int sequenceNumber)
        {
            return string.Format("AT*PCMD={0},{1},{2},{3},{4},{5}\r", sequenceNumber,
                                 (int) _mode,
                                 ConversionHelper.ToInt(_roll),
                                 ConversionHelper.ToInt(_pitch),
                                 ConversionHelper.ToInt(_gaz),
                                 ConversionHelper.ToInt(_yaw));
        }
    }
}