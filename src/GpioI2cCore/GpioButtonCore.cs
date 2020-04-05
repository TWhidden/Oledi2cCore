using System;
using System.Diagnostics;
using System.Threading;

namespace GpioI2cCore
{
    public class GpioButtonCore
    {
        private readonly II2C _i2CWire;
        private readonly GpioPin _pinToMonitor;
        private GpioPressState _lastState = GpioPressState.NotChecked;
        private bool _isRunning = true;

        public GpioButtonCore(II2C i2cWire, GpioPin pinToMonitor)
        {
            _i2CWire = i2cWire;
            _pinToMonitor = pinToMonitor;
        }

        public bool Initialize()
        {
            _isRunning = true;
            var thread = new Thread(ProcessLoop)
            {
                Name = "ButtonPressLoop",
                IsBackground = true
            };
            thread.Start();

            return true;
        }

        public void UnInitialize()
        {
            _isRunning = false;
        }

        private void ProcessLoop()
        {
            while (_isRunning)
            {
                try
                {
                    var result = _i2CWire.GetPinStatus((byte) _pinToMonitor);
                    var newState = result ? GpioPressState.Pressed : GpioPressState.NotPressed;

                    if (newState != _lastState)
                    {
                        _lastState = newState;
                        OnPressState(newState);
                        Debug.WriteLine($"State: {result}");
                    }
                }
                catch
                {
                    // Ignore
                }
                finally
                {
                    Thread.Sleep(50);
                }
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
