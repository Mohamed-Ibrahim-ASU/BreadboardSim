using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace SimGUI
{
    public partial class GraphSettings : Window
    {
        private List<Quantity> VoltOptions;
        private List<Quantity> TimeOptions;
        private List<Quantity> AmpsOptions;

        public GraphSettings()
        {
            InitializeComponent();
            
            VoltOptions = new List<Quantity>();
            for (int magnitude = 1; magnitude >= -4; magnitude--)
            {
                for (int i = 0; i < Constants.PreferredValues.Length; i++)
                {
                    double val = Constants.PreferredValues[i] * Math.Pow(10, magnitude);
                    Quantity q = new Quantity("", "", "V");
                    q.Val = val;
                    VoltOptions.Add(q);
                }
            }

            AmpsOptions = new List<Quantity>();
            for (int magnitude = 1; magnitude >= -4; magnitude--)
            {
                for (int i = 0; i < Constants.PreferredValues.Length; i++)
                {
                    double val = Constants.PreferredValues[i] * Math.Pow(10, magnitude);
                    Quantity q = new Quantity("", "", "A");
                    q.Val = val;
                    AmpsOptions.Add(q);
                }
            }

            TimeOptions = new List<Quantity>();
            for (int magnitude = 1; magnitude >= -9; magnitude--)
            {
                for (int i = 0; i < Constants.PreferredValues.Length; i++)
                {
                    double val = Constants.PreferredValues[i] * Math.Pow(10, magnitude);
                    Quantity q = new Quantity("", "", "s");
                    q.Val = val;
                    TimeOptions.Add(q);
                }
            }

            foreach (Quantity q in VoltOptions)
            {
                VoltsPerDiv.Items.Add(q);
            }
            VoltsPerDiv.SelectedIndex = 5;

            foreach (Quantity q in AmpsOptions)
            {
                AmpsPerDiv.Items.Add(q);
            }
            AmpsPerDiv.SelectedIndex = 5;

            foreach (Quantity q in TimeOptions)
            {
                SecPerDiv.Items.Add(q);
            }
            SecPerDiv.SelectedIndex = 5;
        }

        public void SetSettings(double voltsPerDiv, double voltsOffset, double secPerDiv, double ampsPerDiv, double ampsOffset)
        {
            foreach (Quantity q in TimeOptions)
            {
                if (q.Val == secPerDiv)
                {
                    SecPerDiv.SelectedItem = q;
                }
            }

            foreach (Quantity q in VoltOptions)
            {
                if (q.Val == voltsPerDiv)
                {
                    VoltsPerDiv.SelectedItem = q;
                }
            }

            foreach (Quantity q in AmpsOptions)
            {
                if (q.Val == ampsPerDiv)
                {
                    AmpsPerDiv.SelectedItem = q;
                }
            }

            VoltsOffset.Text = voltsOffset.ToString();
            AmpsOffset.Text = ampsOffset.ToString();
        }

        public Quantity GetVoltsPerDiv()
        {
            return VoltOptions[VoltsPerDiv.SelectedIndex];
        }

        public double GetVoltsOffset()
        {
            return double.Parse(VoltsOffset.Text);
        }

        public Quantity GetAmpsPerDiv()
        {
            return AmpsOptions[AmpsPerDiv.SelectedIndex];
        }

        public double GetAmpsOffset()
        {
            return double.Parse(AmpsOffset.Text);
        }

        public Quantity GetSecPerDiv()
        {
            return TimeOptions[SecPerDiv.SelectedIndex];
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            double tmp = 0;
            if (!double.TryParse(VoltsOffset.Text, out tmp))
            {
                MessageBox.Show("Volts offset must be a valid number.");
                return;
            }
            if (!double.TryParse(AmpsOffset.Text, out tmp))
            {
                MessageBox.Show("Amps offset must be a valid number.");
                return;
            }
            
            DialogResult = true; 
        }

        private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Only validate if the user is actually trying to save (DialogResult == true)
            if (DialogResult == true) 
            {
                double tmp = 0;
                if (!double.TryParse(VoltsOffset.Text, out tmp))
                {
                    MessageBox.Show("Volts offset must be a valid number.");
                    e.Cancel = true;
                }
                if (!double.TryParse(AmpsOffset.Text, out tmp))
                {
                    MessageBox.Show("Amps offset must be a valid number.");
                    e.Cancel = true;
                }
            }
        }
    }
}