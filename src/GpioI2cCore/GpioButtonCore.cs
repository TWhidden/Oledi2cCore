using System;
using System.Diagnostics;
using System.Threading;

namespace GpioI2cCore
{
    public class GpioButtonCore
    {
        private readonly II2C _i2CWire;
        private readonly GpioPin _pinToMonitor;
        private Timer _buttonCheckTimer;
        private GpioPressState _lastState = GpioPressState.NotChecked;

        public GpioButtonCore(II2C i2cWire, GpioPin pinToMonitor)
        {
            _i2CWire = i2cWire;
            _pinToMonitor = pinToMonitor;
        }

        public void Initialize()
        {
            _buttonCheckTimer = new Timer((Callback), null, TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(50));
        }

        public void UnInitialize()
        {
            _buttonCheckTimer.Dispose();
        }

        private void Callback(object state)
        {
            try
            {
                var result = _i2CWire.GetPinStatus((byte) _pinToMonitor);
                var newState = result ? GpioPressState.Pressed : GpioPressState.NotPressed;

                if (newState != _lastState)
                {
                    _lastState = newState;
                    OnPressState(newState);
                }

                Debug.WriteLine($"State: {result}");

            }
            catch (Exception ex)
            {
                
            }
        }

        public event EventHandler<GpioPressState> PressState;


        protected virtual void OnPressState(GpioPressState e)
        {
            PressState?.Invoke(this, e);
        }
    }

    [Flags]
    public enum GpioPin : byte
    {
        Pin1 = 1,
        Pin2 = 2,
        Pin3 = 4,
        Pin4 = 8,
        Pin5 = 16,
        Pin6 = 32,
        Pin7 = 64,
        Pin8 = 128
    }

    public enum GpioPressState
    {
        NotChecked,
        Pressed,
        NotPressed
    }
}
