﻿using System;
using System.Collections.Generic;
using Jacobi.Vst.Core;
using Jacobi.Vst.Interop.Host;
using NAudio.Wave;
using CommonUtils;
using CommonUtils.Audio;

namespace CommonUtils.VST
{
	/// <summary>
	/// Description of VstHost.
	/// </summary>
	public sealed class VstHost
	{
		private int count = 0;
		
		private static readonly VstHost instance = new VstHost();

		public VstAudioBuffer[] vstInputBuffers = null;
		public VstAudioBuffer[] vstOutputBuffers = null;
		public VstPluginContext PluginContext { get; set; }
		
		public int BlockSize { get; set; }
		public int SampleRate { get; set; }
		public int Channels { get; set; }

		// these are used for midi note playing
		public byte SendContinousMidiNoteVelocity = 100;
		public byte SendContinousMidiNote = 72; // C 5
		
		private WaveChannel32 wavStream;
		private WaveFileReader wavFileReader;
		
		private int tailWaitForNumberOfSeconds = 5;
		
		private bool initialisedMainOut = false;
		
		// can be used for spectrum analysis and spectrogram
		private float[] lastProcessedBufferRight;
		private float[] lastProcessedBufferLeft;
		
		// for recording
		private bool doRecord = false;
		private List<float> recordedRight = new List<float>();
		private List<float> recordedLeft = new List<float>();
		
		// Explicit static constructor to tell C# compiler
		// not to mark type as beforefieldinit
		static VstHost()
		{
		}
		
		private VstHost()
		{
		}
		
		public static VstHost Instance
		{
			get { return instance; }
		}
		
		public string InputWave
		{
			set
			{
				// 4 bytes per sample (32 bit)
				this.wavFileReader = new WaveFileReader(value);
				this.wavStream = new WaveChannel32(this.wavFileReader);
				this.wavStream.Volume = 1f;
			}
		}

		public int TailWaitForNumberOfSeconds
		{
			get { return this.tailWaitForNumberOfSeconds; }
			set { this.tailWaitForNumberOfSeconds = value; }
		}
		
		public float[] LastProcessedBufferRight
		{
			get { return this.lastProcessedBufferRight; }
		}

		public float[] LastProcessedBufferLeft
		{
			get { return this.lastProcessedBufferLeft; }
		}

		public bool LastProcessedBufferLeftPlaying
		{
			get {
				return AudioUtils.HasDataAboveThreshold(lastProcessedBufferLeft, 0);
			}
		}
		
		public bool LastProcessedBufferRightPlaying
		{
			get {
				return AudioUtils.HasDataAboveThreshold(lastProcessedBufferRight, 0);
			}
		}
		
		public List<float> RecordedRight
		{
			get { return this.recordedRight; }
			set { this.recordedRight = value; }
		}

		public List<float> RecordedLeft
		{
			get { return this.recordedLeft; }
			set { this.recordedLeft = value; }
		}
		
		public bool Record {
			get { return this.doRecord; }
			set {this.doRecord = value; }
		}

		public void DisposeInputWave() {
			
			if (wavStream != null) {
				this.wavStream.Dispose();
				this.wavStream = null;
			}
			this.wavFileReader = null;
		}
		
		public void ClearRecording() {
			this.recordedLeft.Clear();
			this.recordedRight.Clear();
		}
		
		public void ClearLastProcessedBuffers() {
			for (int i = 0; i < this.lastProcessedBufferRight.Length; i++) {
				this.lastProcessedBufferRight[i] = 0.0f;
			}
			for (int i = 0; i < this.lastProcessedBufferLeft.Length; i++) {
				this.lastProcessedBufferLeft[i] = 0.0f;
			}
		}
		
		public void OpenPlugin(string pluginPath, Jacobi.Vst.Core.Host.IVstHostCommandStub hostCmdStub)
		{
			try
			{
				VstPluginContext ctx = VstPluginContext.Create(pluginPath, hostCmdStub);

				// add custom data to the context
				ctx.Set("PluginPath", pluginPath);
				ctx.Set("HostCmdStub", hostCmdStub);

				// actually open the plugin itself
				ctx.PluginCommandStub.Open();
				PluginContext = ctx;
				
				// there is a question whether we should "turn on" the plugin here or later
				// by "turn on" i mean do MainsChanged(true)
				// a working Vst Host (see MidiVstTest, a copy from the microDRUM project)
				// does the following:
				// 
				// GeneralVST.pluginContext = VstPluginContext.Create(VSTPath, hcs);
				// GeneralVST.pluginContext.PluginCommandStub.Open();
				// GeneralVST.pluginContext.PluginCommandStub.EditorOpen(hWnd);
				// GeneralVST.pluginContext.PluginCommandStub.MainsChanged(true);
				
				// While a forum entry suggested the following:
				// [plugin.Open()]
				// plugin.MainsChanged(true) // turn on 'power' on plugin.
				// plugin.StartProcess() // let the plugin know the audio engine has started
				// PluginContext.PluginCommandStub.ProcessEvents(ve); // process events (like VstMidiEvent)
				// 
				// while(audioEngineIsRunning)
				// {
				//     plugin.ProcessReplacing(inputBuffers, outputBuffers)  // letplugin process audio stream
				// }
				// 
				// plugin.StopProcess()
				// plugin.MainsChanged(false)
				// 
				// [plugin.Close()]
				
				//doPluginOpen();
			}
			catch (Exception e)
			{
				throw new InvalidOperationException(e.ToString(), e.InnerException);
			}
		}

		private void ReleasePlugin()
		{
			doPluginClose();
			PluginContext.PluginCommandStub.Close();

			// dispose of all (unmanaged) resources
			PluginContext.Dispose();
		}
		
		public void Init(int blockSize, int sampleRate, int channels)
		{
			int inputCount = PluginContext.PluginInfo.AudioInputCount;
			int outputCount = PluginContext.PluginInfo.AudioOutputCount;
			this.BlockSize = blockSize;
			this.SampleRate = sampleRate;
			this.Channels = channels;
			
			InitBuffer(inputCount, outputCount, blockSize, sampleRate);
		}
		
		private void InitBuffer(int inputCount, int outputCount, int blockSize, int sampleRate)
		{
			var inputMgr = new VstAudioBufferManager(inputCount, blockSize);
			var outputMgr = new VstAudioBufferManager(outputCount, blockSize);
			
			this.vstInputBuffers = inputMgr.ToArray();
			this.vstOutputBuffers = outputMgr.ToArray();

			this.PluginContext.PluginCommandStub.SetBlockSize(blockSize);
			this.PluginContext.PluginCommandStub.SetSampleRate((float)sampleRate);
			this.PluginContext.PluginCommandStub.SetProcessPrecision(VstProcessPrecision.Process32);
			
			this.lastProcessedBufferRight = new float[BlockSize];
			this.lastProcessedBufferLeft = new float[BlockSize];
		}
		
		public void doPluginOpen() {
			// The calls to MainsChanged and Start/Stop Process should be made only once, not for every cycle in the audio processing.
			// So it should look something like:
			// 
			// [plugin.Open()]
			// plugin.MainsChanged(true) // turn on 'power' on plugin.
			// plugin.StartProcess() // let the plugin know the audio engine has started
			// PluginContext.PluginCommandStub.ProcessEvents(ve); // process events (like VstMidiEvent)
			// 
			// while(audioEngineIsRunning)
			// {
			//     plugin.ProcessReplacing(inputBuffers, outputBuffers)  // letplugin process audio stream
			// }
			// 
			// plugin.StopProcess()
			// plugin.MainsChanged(false)
			// 
			// [plugin.Close()]

			if (initialisedMainOut == false)
			{
				// Open Resources
				PluginContext.PluginCommandStub.MainsChanged(true);
				PluginContext.PluginCommandStub.StartProcess();
				initialisedMainOut = true;
			}
		}

		public void doPluginClose() {
			// The calls to Mainschanged and Start/Stop Process should be made only once, not for every cycle in the audio processing.
			// So it should look something like:
			// 
			// [plugin.Open()]
			// plugin.MainsChanged(true) // turn on 'power' on plugin.
			// plugin.StartProcess() // let the plugin know the audio engine has started
			// PluginContext.PluginCommandStub.ProcessEvents(ve); // process events (like VstMidiEvent)
			// 
			// while(audioEngineIsRunning)
			// {
			//     plugin.ProcessReplacing(inputBuffers, outputBuffers)  // letplugin process audio stream
			// }
			// 
			// plugin.StopProcess()
			// plugin.MainsChanged(false)
			// 
			// [plugin.Close()]

			PluginContext.PluginCommandStub.StopProcess();
			PluginContext.PluginCommandStub.MainsChanged(false);
		}
		
		// This function fills vstOutputBuffers with audio processed by a plugin
		public int ProcessReplacing(uint sampleCount) {
			int loopSize = (int) sampleCount / Channels;

			lock (this)
			{

				// check if we are processing a wavestream (VST) or if this is audio outputting only (VSTi)
				if (wavStream != null) {
					int sampleCountx4 = (int) sampleCount * 4;
					
					// 4 bytes per sample (32 bit)
					var naudioBuf = new byte[sampleCountx4];
					int bytesRead = wavStream.Read(naudioBuf, 0, sampleCountx4);

					if (wavStream.CurrentTime > wavStream.TotalTime.Add(TimeSpan.FromSeconds(tailWaitForNumberOfSeconds))) {
						return 0;
					}
					
					// populate the inputbuffers with the incoming wave stream
					// TODO: do not use unsafe - but like this http://vstnet.codeplex.com/discussions/246206 ?
					// this whole section is modelled after http://vstnet.codeplex.com/discussions/228692
					unsafe
					{
						fixed (byte* byteBuf = &naudioBuf[0])
						{
							float* floatBuf = (float*)byteBuf;
							int j = 0;
							for (int i = 0; i < loopSize; i++)
							{
								vstInputBuffers[0][i] = *(floatBuf + j); // left
								j++;
								vstInputBuffers[1][i] = *(floatBuf + j); // right
								j++;
							}
						}
					}
				}
				
				// make sure the plugin has been opened.
				doPluginOpen();
				
				// and do the vst processing
				try
				{
					PluginContext.PluginCommandStub.ProcessReplacing(vstInputBuffers, vstOutputBuffers);
				}
				catch (Exception)
				{
				}

				// store the output into the last processed buffers
				for (int channelNumber = 0; channelNumber < Channels; channelNumber++)
				{
					for (int samples = 0; samples < vstOutputBuffers[channelNumber].SampleCount; samples++)
					{
						switch (channelNumber) {
							case 0:
								lastProcessedBufferLeft[samples] = vstOutputBuffers[channelNumber][samples];
								break;
							case 1:
								lastProcessedBufferRight[samples] = vstOutputBuffers[channelNumber][samples];
								break;
						}
					}
				}
				
				// Record audio
				if (doRecord) {
					recordedRight.AddRange(lastProcessedBufferLeft);
					recordedLeft.AddRange(lastProcessedBufferRight);
				}
				
				count++;
			}
			return (int) sampleCount;
		}
		
		public void SaveWavFile(string fileName) {
			WaveFileWriter.CreateWaveFile(fileName, this.wavStream);
		}
		
		public void SetProgram(int programNumber) {
			if (programNumber < PluginContext.PluginInfo.ProgramCount && programNumber >= 0)
			{
				PluginContext.PluginCommandStub.SetProgram(programNumber);
			}
		}

		private VstEvent[] CreateMidiEvent(byte statusByte, byte midiNote, byte midiVelocity) {
			/* 
			 * Just a small note on the code for setting up a midi event:
			 * You can use the VstEventCollection class (Framework) to setup one or more events
			 * and then call the ToArray() method on the collection when passing it to
			 * ProcessEvents. This will save you the hassle of dealing with arrays explicitly.
			 * http://computermusicresource.com/MIDI.Commands.html
			 * 
			 * Freq to Midi notes etc:
			 * http://www.sengpielaudio.com/calculator-notenames.htm
			 * 
			 * Example to use NAudio Midi support
			 * http://stackoverflow.com/questions/6474388/naudio-and-midi-file-reading
			 */
			var midiData = new byte[4];
			
			midiData[0] = statusByte;
			midiData[1] = midiNote;   	// Midi note
			midiData[2] = midiVelocity; // Note strike velocity
			midiData[3] = 0;    		// Reserved, unused
			
			var vse = new VstMidiEvent(/*DeltaFrames*/ 	0,
			                           /*NoteLength*/ 	0,
			                           /*NoteOffset*/ 	0,
			                           midiData,
			                           /*Detune*/    		0,
			                           /*NoteOffVelocity*/ 127); // previously 0
			
			var ve = new VstEvent[1];
			ve[0] = vse;
			return ve;
		}
		
		public void SendMidiNote(byte statusByte, byte midiNote, byte midiVelocity) {
			// make sure the plugin has been opened.
			doPluginOpen();
			VstEvent[] vEvent = CreateMidiEvent(statusByte, midiNote, midiVelocity);
			PluginContext.PluginCommandStub.ProcessEvents(vEvent);
		}
		
		public void SendMidiNote(byte midiNote, byte midiVelocity) {
			SendMidiNote(MidiUtils.CHANNEL1_NOTE_ON, midiNote, midiVelocity);
		}
		
		public string getPluginInfo() {
			if (PluginContext != null) {
				var pluginInfo = new List<string>(); // Create new list of strings
				
				// plugin product
				pluginInfo.Add("Plugin Name " +  PluginContext.PluginCommandStub.GetEffectName());
				pluginInfo.Add("Product " +  PluginContext.PluginCommandStub.GetProductString());
				pluginInfo.Add("Vendor " +  PluginContext.PluginCommandStub.GetVendorString());
				pluginInfo.Add("Vendor Version " +  PluginContext.PluginCommandStub.GetVendorVersion().ToString());
				pluginInfo.Add("Vst Support " +  PluginContext.PluginCommandStub.GetVstVersion().ToString());
				pluginInfo.Add("Plugin Category " +  PluginContext.PluginCommandStub.GetCategory().ToString());
				
				// plugin info
				pluginInfo.Add("Flags " +  PluginContext.PluginInfo.Flags.ToString());
				pluginInfo.Add("Plugin ID " +  PluginContext.PluginInfo.PluginID.ToString());
				pluginInfo.Add("Plugin Version " +  PluginContext.PluginInfo.PluginVersion.ToString());
				pluginInfo.Add("Audio Input Count " +  PluginContext.PluginInfo.AudioInputCount.ToString());
				pluginInfo.Add("Audio Output Count " +  PluginContext.PluginInfo.AudioOutputCount.ToString());
				pluginInfo.Add("Initial Delay " +  PluginContext.PluginInfo.InitialDelay.ToString());
				pluginInfo.Add("Program Count " +  PluginContext.PluginInfo.ProgramCount.ToString());
				pluginInfo.Add("Parameter Count " +  PluginContext.PluginInfo.ParameterCount.ToString());
				pluginInfo.Add("Tail Size " + PluginContext.PluginCommandStub.GetTailSize().ToString());
				
				// can do
				pluginInfo.Add("CanDo: " + VstPluginCanDo.Bypass + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.Bypass)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.MidiProgramNames + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.MidiProgramNames)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.Offline + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.Offline)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.ReceiveVstEvents + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.ReceiveVstEvents)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.ReceiveVstMidiEvent + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.ReceiveVstMidiEvent)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.ReceiveVstTimeInfo + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.ReceiveVstTimeInfo)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.SendVstEvents + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.SendVstEvents)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.SendVstMidiEvent + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.SendVstMidiEvent)).ToString());
				
				pluginInfo.Add("CanDo: " + VstPluginCanDo.ConformsToWindowRules + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.ConformsToWindowRules)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.Metapass + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.Metapass)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.MixDryWet + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.MixDryWet)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.Multipass + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.Multipass)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.NoRealTime + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.NoRealTime)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.PlugAsChannelInsert + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.PlugAsChannelInsert)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.PlugAsSend + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.PlugAsSend)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.SendVstTimeInfo + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.SendVstTimeInfo)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x1in1out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x1in1out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x1in2out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x1in2out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x2in1out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x2in1out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x2in2out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x2in2out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x2in4out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x2in4out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x4in2out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x4in2out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x4in4out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x4in4out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x4in8out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x4in8out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x8in4out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x8in4out)).ToString());
				pluginInfo.Add("CanDo: " + VstPluginCanDo.x8in8out + PluginContext.PluginCommandStub.CanDo(VstCanDoHelper.ToString(VstPluginCanDo.x8in8out)).ToString());
				
				pluginInfo.Add("Program: " + PluginContext.PluginCommandStub.GetProgram());
				pluginInfo.Add("Program Name: " + PluginContext.PluginCommandStub.GetProgramName());

				for (int i = 0; i < PluginContext.PluginInfo.ParameterCount; i++)
				{
					string name = PluginContext.PluginCommandStub.GetParameterName(i);
					string label = PluginContext.PluginCommandStub.GetParameterLabel(i);
					string display = PluginContext.PluginCommandStub.GetParameterDisplay(i);
					bool canBeAutomated = PluginContext.PluginCommandStub.CanParameterBeAutomated(i);
					
					pluginInfo.Add(String.Format("Parameter Index: {0} Parameter Name: {1} Display: {2} Label: {3} Can be automated: {4}", i, name, display, label, canBeAutomated));
				}
				return string.Join("\n", pluginInfo.ToArray());
			}
			return "Nothing";
		}
		
		public void ShowPluginEditor()
		{
			/*
			EditorFrame dlg = new EditorFrame();
			dlg.PluginContext = PluginContext;

			PluginContext.PluginCommandStub.MainsChanged(true);
			dlg.ShowDialog();
			PluginContext.PluginCommandStub.MainsChanged(false);
			 */
		}

		public void LoadFXP(string filePath) {
			if (string.IsNullOrEmpty(filePath)) {
				return;
			}
			// How does the GetChunk/SetChunk interface work? What information should be in those chunks?
			// How does the BeginLoadProgram and BeginLoadBank work?
			// There doesn't seem to be any restriction on what data is put in the chunks.
			// The beginLoadBank/Program methods are also part of the persistence call sequence.
			// GetChunk returns a buffer with program information of either the current/active program
			// or all programs.
			// SetChunk should read this information back in and initialize either the current/active program
			// or all programs.
			// Before SetChunk is called, the beginLoadBank/Program method is called
			// passing information on the version of the plugin that wrote the data.
			// This will allow you to support older data versions of your plugin's data or
			// even support reading other plugin's data.
			// Some hosts will call GetChunk before calling beginLoadBakn/Program and SetChunk.
			// This is an optimazation of the host to determine if the information to load is
			// actually different than the state your plugin program(s) (are) in.

			bool UseChunk = false;
			if ((PluginContext.PluginInfo.Flags & VstPluginFlags.ProgramChunks) == 0) {
				// Chunks not supported.
				UseChunk = false;
			} else {
				// Chunks supported.
				UseChunk = true;
			}
			
			var fxp = new FXP();
			fxp.ReadFile(filePath);
			if (fxp.ChunkMagic != "CcnK") {
				// not a fxp or fxb file
				Console.Out.WriteLine("Error - Cannot Load. Loaded preset is not a fxp or fxb file");
				return;
			}

			int pluginUniqueID = PluginIDStringToIDNumber(fxp.FxID);
			int currentPluginID = PluginContext.PluginInfo.PluginID;
			if (pluginUniqueID != currentPluginID) {
				Console.Out.WriteLine("Error - Cannot Load. Loaded preset has another ID!");
			} else {
				// Preset (Program) (.fxp) with chunk (magic = 'FPCh')
				// Bank (.fxb) with chunk (magic = 'FBCh')
				if (fxp.FxMagic == "FPCh" || fxp.FxMagic == "FBCh") {
					UseChunk = true;
				} else {
					UseChunk = false;
				}
				if (UseChunk) {
					// If your plug-in is configured to use chunks
					// the Host will ask for a block of memory describing the current
					// plug-in state for saving.
					// To restore the state at a later stage, the same data is passed
					// back to setChunk.
					byte[] chunkData = fxp.ChunkDataByteArray;
					bool beginSetProgramResult = PluginContext.PluginCommandStub.BeginSetProgram();
					int iResult = PluginContext.PluginCommandStub.SetChunk(chunkData, true);
					bool endSetProgramResult = PluginContext.PluginCommandStub.EndSetProgram();
				} else {
					// Alternatively, when not using chunk, the Host will simply
					// save all parameter values.
					float[] parameters = fxp.Parameters;
					bool beginSetProgramResult = PluginContext.PluginCommandStub.BeginSetProgram();
					for (int i = 0; i < parameters.Length; i++) {
						PluginContext.PluginCommandStub.SetParameter(i, parameters[i]);
					}
					bool endSetProgramResult = PluginContext.PluginCommandStub.EndSetProgram();
				}
			}
		}

		public void SaveFXP(string filePath) {

			bool UseChunk = false;
			if ((PluginContext.PluginInfo.Flags & VstPluginFlags.ProgramChunks) == 0) {
				// Chunks not supported.
				UseChunk = false;
			} else {
				// Chunks supported.
				UseChunk = true;
			}

			var fxp = new FXP();
			fxp.ChunkMagic = "CcnK";
			fxp.ByteSize = 0; // will be set correctly by FXP class
			
			if (UseChunk) {
				// Preset (Program) (.fxp) with chunk (magic = 'FPCh')
				fxp.FxMagic = "FPCh"; // FPCh = FXP (preset), FBCh = FXB (bank)
				fxp.Version = 1; // Format Version (should be 1)
				fxp.FxID = PluginIDNumberToIDString(PluginContext.PluginInfo.PluginID);
				fxp.FxVersion = PluginContext.PluginInfo.PluginVersion;
				fxp.ProgramCount = PluginContext.PluginInfo.ProgramCount;
				fxp.Name = PluginContext.PluginCommandStub.GetProgramName();
				
				byte[] chunkData = PluginContext.PluginCommandStub.GetChunk(true);
				fxp.ChunkSize = chunkData.Length;
				fxp.ChunkDataByteArray = chunkData;
			} else {
				// Preset (Program) (.fxp) without chunk (magic = 'FxCk')
				fxp.FxMagic = "FxCk"; // FxCk = FXP (preset), FxBk = FXB (bank)
				fxp.Version = 1; // Format Version (should be 1)
				fxp.FxID = PluginIDNumberToIDString(PluginContext.PluginInfo.PluginID);
				fxp.FxVersion = PluginContext.PluginInfo.PluginVersion;
				fxp.ParameterCount = PluginContext.PluginInfo.ParameterCount;
				fxp.Name = PluginContext.PluginCommandStub.GetProgramName();

				// variable no. of parameters
				var parameters = new float[fxp.ParameterCount];
				for (int i = 0; i < fxp.ParameterCount; i++) {
					parameters[i] = PluginContext.PluginCommandStub.GetParameter(i);
				}
				fxp.Parameters = parameters;
			}
			fxp.WriteFile(filePath);
		}
		
		private static string PluginIDNumberToIDString(int pluginUniqueID) {
			byte[] fxIdArray = BitConverter.GetBytes(pluginUniqueID);
			Array.Reverse(fxIdArray);
			string fxIdString = BinaryFile.ByteArrayToString(fxIdArray);
			return fxIdString;
		}

		private static int PluginIDStringToIDNumber(string fxIdString) {
			byte[] pluginUniqueIDArray = BinaryFile.StringToByteArray(fxIdString); // 58h8 = 946354229
			Array.Reverse(pluginUniqueIDArray);
			int pluginUniqueID = BitConverter.ToInt32(pluginUniqueIDArray, 0);
			return pluginUniqueID;
		}
	}
}
