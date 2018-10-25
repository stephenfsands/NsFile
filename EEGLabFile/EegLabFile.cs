using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using NSFileAccess;

namespace EegLabFile
{
    public class EegLab : NsFile
    {
        private ulong _numContinuousSamples;
        public void ReadHeaderFile(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var path = Path.GetDirectoryName(fileName);
            var binHeaderFileName = path + "\\" + name + ".head";

            using (var rdr = new StreamReader(binHeaderFileName))
            {
                string line;
                while ((line = rdr.ReadLine()) != null)
                {

                    if (line.Contains("start_ts"))
                    {
                        var date = line.Substring(line.IndexOf('s') + 9).Split(' ');
                        var time = line.Substring(line.IndexOf('s') + 20).Split('n');
                        Date = date[0];
                        Time = time[0];
                        continue;
                    }

                    if (line.Contains("num_samples"))
                    {
                        var numSamples = line.Substring(line.IndexOf('=') + 1).Split('s');
                        _numContinuousSamples = ulong.Parse(numSamples[0]);
                        continue;
                    }

                    if (line.Contains("sample_freq"))
                    {
                        var samplesFreq = line.Substring(line.IndexOf('=') + 1).Split('c');
                        AtodRate = uint.Parse(samplesFreq[0]);
                        continue;
                    }

                    if (line.Contains("conversion_factor"))
                    {
                        var conversionFactor = line.Substring(line.IndexOf('=') + 1).Split('c');
                        CalibrationScaleFactor = double.Parse(conversionFactor[0]);
                        continue;
                    }

                    if (line.Contains("num_channels"))
                    {
                        var numChannels = line.Substring(line.IndexOf('=') + 1).Split('e');
                        NumberOfChannels = uint.Parse(numChannels[0]);
                        continue;
                    }

                    if (line.Contains("elec_names"))
                    {
                        // This will retrieve the electrode label and put them in the nsfile object
                        var elect = line.Substring(line.IndexOf('[') + 1)
                            .Split(']'); 
                        var electNames = string.Join(",", elect);
                        var electLab = electNames.Split(',');
                        for (var i = 0; i < NumberOfChannels; ++i)
                        {
                            SetElectrodeLabel(i, electLab[i]); 
                        }
                        // if the object is filled then we will stuff the calibration values into the same stucture
                        // of the already allocated electrodes.  This is because we just have electrodes and we can't 
                        // do it above with the calibration.  NS stores calibration values indivdually for each channel 
                        // is not a concept of global calibration. 
                        for (var i = 0; i < NumberOfChannels; ++i)
                        {
                            SetElectrodeCalibration(i, (float)CalibrationScaleFactor);
                        }

                        continue;
                    }

                    if (line.Contains("pat_id"))
                    {
                        var patId = line.Substring(line.IndexOf('=') + 1).Split('a');
                        Id = patId[0];
                        Debug.WriteLine(Id);
                    }

                    if (line.Contains("adm_id"))
                    {
                        var admId = line.Substring(line.IndexOf('=') + 1).Split('r');
                        Doctor = admId[0];
                    }

                    if (line.Contains("rec_id"))
                    {
                        var recId = line.Substring(line.IndexOf('=') + 1).Split('d');
                        Patient = recId[0];
                    }

                    if (line.Contains("duration_in_sec"))
                    {
                        var durationInSec = line.Substring(line.IndexOf('=') + 1).Split('s');
                    }

                    if (line.Contains("sample_bytes"))
                    {
                        var sampleBytes = line.Substring(line.IndexOf('=') + 1).Split(' ');
                    }
                }
            }
        }

        // Data reading
        public void ReadDataFile(string fileName)
        {
            using (var reader = new BinaryReader(File.Open(fileName, FileMode.Open)))
            {
                var length = (int) reader.BaseStream.Length;
                long pos = 0;
                var samples = new float[_numContinuousSamples];
                //float[,] reshapedSamples = new float[1, numbersamples];
                while (pos < length)
                {
                    for (ulong i = 0; i < _numContinuousSamples; i++)
                    {
                        samples[i] = reader.ReadInt32();
                        WriteCntData();

                        pos = reader.BaseStream.Position;
                        reader.BaseStream.Seek(pos, SeekOrigin.Begin);

                        //for (int j = 0; j < reshapedSamples.GetLength(0); j++)
                        //{
                        //    for (int k = 0; k < reshapedSamples.GetLength(1); k++)
                        //    {
                        //        reshapedSamples[j, k] = samples[j];

                        //    }
                        //}
                    }
                }
            }
        }
    }
}






































