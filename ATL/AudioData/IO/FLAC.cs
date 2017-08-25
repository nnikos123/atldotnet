using System;
using System.IO;
using System.Collections.Generic;
using static ATL.AudioData.MetaDataIO;
using Commons;
using static ATL.AudioData.FileStructureHelper;
using System.Text;

namespace ATL.AudioData.IO
{
    /// <summary>
    /// Class for Free Lossless Audio Codec files manipulation (extension : .FLAC)
    /// </summary>
	class FLAC : IMetaDataIO, IAudioDataIO
	{

		private const byte META_STREAMINFO      = 0;
		private const byte META_PADDING         = 1;
		private const byte META_APPLICATION     = 2;
		private const byte META_SEEKTABLE       = 3;
		private const byte META_VORBIS_COMMENT  = 4;
		private const byte META_CUESHEET        = 5;
        private const byte META_PICTURE         = 6;

        private const string FLAC_ID = "fLaC";

        private const string ZONE_VORBISTAG = "VORBISTAG";
        private const string ZONE_PICTURE = "PICTURE";


        private class FlacHeader
		{
            public string StreamMarker;
			public byte[] MetaDataBlockHeader = new byte[4];
			public byte[] Info = new byte[18];
			// 16-bytes MD5 Sum only applies to audio data
    
			public void Reset()
			{
                StreamMarker = "";
				Array.Clear(MetaDataBlockHeader,0,4);
				Array.Clear(Info,0,18);
			}

            public bool IsValid()
            {
                return StreamMarker.Equals(FLAC_ID);
            }

            public FlacHeader()
            {
                Reset();
            }
		}

        private readonly string filePath;
        private AudioDataManager.SizeInfo sizeInfo;

        private VorbisTag vorbisTag;
        
        private FlacHeader header;
        IList<FileStructureHelper.Zone> zones; // That's one hint of why interactions with VorbisTag need to be redesigned...

        // Internal metrics
        private int paddingIndex;
		private bool paddingLast;
		private uint padding;
		private long audioOffset;
        private long firstBlockPosition;

        // Physical info
		private byte channels;
		private int sampleRate;
		private byte bitsPerSample;
		private long samples;


		public byte Channels // Number of channels
		{
			get { return channels; }
		}
		public int SampleRate // Sample rate (hz)
		{
			get { return sampleRate; }
		}
		public byte BitsPerSample // Bits per sample
		{
			get { return bitsPerSample; }
		}
		public long Samples // Number of samples
		{
			get { return samples; }
		}
		public double Ratio // Compression ratio (%)
		{
			get { return getCompressionRatio(); }
		}
        public bool IsVBR
        {
			get { return false; }
		}
        public String ChannelMode 
		{
			get { return getChannelMode(); }
		}
		public bool Exists 
		{
			get { return vorbisTag.Exists; }
		}
		public long AudioOffset //offset of audio data
		{
			get { return audioOffset; }
		}
        public string FileName
        {
            get { return filePath; }
        }
        public double BitRate
        {
            get { return Math.Round( ((double)(sizeInfo.FileSize - audioOffset)) * 8 / Duration / 1000.0 ); }
        }
        public double Duration
        {
            get { return getDuration(); }
        }


        // ---------- CONSTRUCTORS & INITIALIZERS

        protected void resetData()
        {
            // Audio data
			padding = 0;
			paddingLast = false;
			channels = 0;
			sampleRate = 0;
			bitsPerSample = 0;
			samples = 0;
			paddingIndex = 0;
			audioOffset = 0;
		}

        public FLAC(string path)
        {
            filePath = path;
            header = new FlacHeader();
            resetData();
        }

        // ---------- INFORMATIVE INTERFACE IMPLEMENTATIONS & MANDATORY OVERRIDES

        public int CodecFamily
        {
            get { return AudioDataIOFactory.CF_LOSSLESS; }
        }
        public bool AllowsParsableMetadata
        {
            get { return true; }
        }

        #region IMetaDataReader
        public string Title
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Title;
            }
        }

        public string Artist
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Artist;
            }
        }

        public string Composer
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Composer;
            }
        }

        public string Comment
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Comment;
            }
        }

        public string Genre
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Genre;
            }
        }

        public ushort Track
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Track;
            }
        }

        public ushort Disc
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Disc;
            }
        }

        public string Year
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Year;
            }
        }

        public string Album
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Album;
            }
        }

        public ushort Rating
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Rating;
            }
        }

        public string Copyright
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Copyright;
            }
        }

        public string OriginalArtist
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).OriginalArtist;
            }
        }

        public string OriginalAlbum
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).OriginalAlbum;
            }
        }

        public string GeneralDescription
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).GeneralDescription;
            }
        }

        public string Publisher
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Publisher;
            }
        }

        public string AlbumArtist
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).AlbumArtist;
            }
        }

        public string Conductor
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Conductor;
            }
        }

        public IList<TagData.PictureInfo> PictureTokens
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).PictureTokens;
            }
        }

        public int Size
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).Size;
            }
        }

        public IDictionary<string, string> AdditionalFields
        {
            get
            {
                return ((IMetaDataIO)vorbisTag).AdditionalFields;
            }
        }
        #endregion

        public bool IsMetaSupported(int metaDataType)
        {
            return (metaDataType == MetaDataIOFactory.TAG_NATIVE || metaDataType == MetaDataIOFactory.TAG_ID3V2);
        }
        public bool HasNativeMeta()
        {
            return true; // Native is for VorbisTag
        }



        /* -------------------------------------------------------------------------- */
        
        // Check for right FLAC file data
        private bool isValid()
		{
			return ( ( header.IsValid() ) &&
				(channels > 0) &&
				(sampleRate > 0) &&
				(bitsPerSample > 0) &&
				(samples > 0) );
		}

        private void readHeader(BinaryReader source)
        {
            source.BaseStream.Seek(sizeInfo.ID3v2Size, SeekOrigin.Begin);

            // Read header data    
            header.Reset();

            header.StreamMarker = Utils.Latin1Encoding.GetString(source.ReadBytes(4));
            header.MetaDataBlockHeader = source.ReadBytes(4);
            header.Info = source.ReadBytes(18);
            source.BaseStream.Seek(16, SeekOrigin.Current); // MD5 sum for audio data
        }

		private double getDuration()
		{
			if ( (isValid()) && (sampleRate > 0) )  
			{
				return (double)samples / sampleRate;
			} 
			else 
			{
				return 0;
			}
		}

		//   Get compression ratio
		private double getCompressionRatio()
		{
			if (isValid()) 
			{
				return (double)sizeInfo.FileSize / (samples * channels * bitsPerSample / 8.0) * 100;
			} 
			else 
			{
				return 0;
			}
		}

		//   Get channel mode
		private String getChannelMode()
		{
			String result;
			if (isValid())
			{
				switch(channels)
				{
					case 1 : result = "Mono"; break;
					case 2 : result = "Stereo"; break;
					default: result = "Multi Channel"; break;
				}
			} 
			else 
			{
				result = "";
			}
			return result;
		}

        public bool Read(BinaryReader source, AudioDataManager.SizeInfo sizeInfo, ReadTagParams readTagParams)
        {
            this.sizeInfo = sizeInfo;

            return read(source, readTagParams);
        }

        public bool Read(BinaryReader source, ReadTagParams readTagParams)
        {
            return read(source, readTagParams);
        }

        private bool read(BinaryReader source, ReadTagParams readTagParams)
        {
            bool result = false;

            if (readTagParams.ReadTag && null == vorbisTag) vorbisTag = new VorbisTag(false, false);

            byte[] aMetaDataBlockHeader;
            long position;
            uint blockLength;
			int blockType;
			int blockIndex;
            bool isLast;
            bool bPaddingFound = false;

            readHeader(source);

			// Process data if loaded and header valid    
			if ( header.IsValid() )
			{
				channels      = (byte)( ((header.Info[12] >> 1) & 0x7) + 1 );
				sampleRate    = ( header.Info[10] << 12 | header.Info[11] << 4 | header.Info[12] >> 4 );
				bitsPerSample = (byte)( ((header.Info[12] & 1) << 4) | (header.Info[13] >> 4) + 1 );
				samples       = ( header.Info[14] << 24 | header.Info[15] << 16 | header.Info[16] << 8 | header.Info[17] );

				if ( 0 == (header.MetaDataBlockHeader[1] & 0x80) ) // metadata block exists
				{
					blockIndex = 0;
                    if (readTagParams.PrepareForWriting)
                    {
                        if (null == zones) zones = new List<Zone>(); else zones.Clear();
                        firstBlockPosition = source.BaseStream.Position;
                    }

                    do // read more metadata blocks if available
					{
						aMetaDataBlockHeader = source.ReadBytes(4);
                        isLast = ((aMetaDataBlockHeader[0] & 0x80) > 0); // last flag ( first bit == 1 )

                        blockIndex++;
                        blockLength = StreamUtils.DecodeBEUInt24(aMetaDataBlockHeader, 1);

						blockType = (aMetaDataBlockHeader[0] & 0x7F); // decode metablock type
                        position = source.BaseStream.Position;

						if ( blockType == META_VORBIS_COMMENT ) // Vorbis metadata
						{
                            if (readTagParams.PrepareForWriting) zones.Add(new Zone(ZONE_VORBISTAG, position - 4, (int)blockLength+4, new byte[0], (byte)(isLast ? 1 : 0)));
                            vorbisTag.Read(source, readTagParams);
						}
						else if ((blockType == META_PADDING) && (! bPaddingFound) )  // Padding block
						{ 
							padding = blockLength;                                            // if we find more skip & put them in metablock array
							paddingLast = ((aMetaDataBlockHeader[0] & 0x80) != 0);
							paddingIndex = blockIndex;
							bPaddingFound = true;
                            source.BaseStream.Seek(padding, SeekOrigin.Current); // advance into file till next block or audio data start
						}
                        else if (blockType == META_PICTURE)
                        {
                            if (readTagParams.PrepareForWriting) zones.Add(new Zone(ZONE_PICTURE, position - 4, (int)blockLength+4, new byte[0], (byte)(isLast ? 1 : 0)));
                            vorbisTag.ReadPicture(source.BaseStream, readTagParams);
                        }
                        // TODO : APPLICATION and CUESHEET blocks

                        if (blockType < 7)
                        {
                            source.BaseStream.Seek(position + blockLength, SeekOrigin.Begin);
                        }
                        else
                        {
                            // Abnormal header : incorrect size and/or misplaced last-metadata-block flag
                            break;
                        }
					}
					while ( !isLast );

                    if (readTagParams.PrepareForWriting)
                    {
                        bool vorbisTagFound = false;
                        bool pictureFound = false;

                        foreach(Zone zone in zones)
                        {
                            if (zone.Name.Equals(ZONE_PICTURE)) pictureFound = true;
                            else if (zone.Name.Equals(ZONE_VORBISTAG)) vorbisTagFound = true;
                        }

                        if (!vorbisTagFound) zones.Add(new Zone(ZONE_VORBISTAG, firstBlockPosition, 0, new byte[0]));
                        if (!pictureFound) zones.Add(new Zone(ZONE_PICTURE, firstBlockPosition, 0, new byte[0]));
                    }
				}
			}

            if (isValid())
            {
                audioOffset = source.BaseStream.Position;  // we need that to rebuild the file if nedeed
                result = true;
            }

			return result;  
		}

        // NB : This only works if writeVorbisTag is called _before_ writePictures, since tagData fusion is done by vorbisTag.Write
        public bool Write(BinaryReader r, BinaryWriter w, TagData tag)
        {
            bool result = true;
            int oldTagSize;
            long newTagSize;
            bool pictureBlockFound = false;
            long cumulativeDelta = 0;

            // Read all the fields in the existing tag (including unsupported fields)
            ReadTagParams readTagParams = new ReadTagParams(null, true);
            readTagParams.PrepareForWriting = true;
            read(r, readTagParams);

            // Rewrite vorbis tag zone
            foreach (Zone zone in zones)
            {
                oldTagSize = zone.Size;

                // Write new tag to a MemoryStream
                using (MemoryStream s = new MemoryStream(zone.Size))
                using (BinaryWriter msw = new BinaryWriter(s, Encoding.UTF8)) // TODO make default UTF-8 encoding customizable
                {
                    if (zone.Name.Equals(ZONE_VORBISTAG)) writeVorbisTag(msw, tag, 1 == zone.Flag);
                    else if (zone.Name.Equals(ZONE_PICTURE))
                    {
                        if (!pictureBlockFound) // All pictures are written at the position of the 1st picture block
                        {
                            pictureBlockFound = true;
                            writePictures(msw, vorbisTag.Pictures, 1 == zone.Flag);
                        } else
                        {
                            s.SetLength(0); // Other picture blocks are erased
                        }
                    }

                    newTagSize = s.Length;

                    // -- Adjust tag slot to new size in file --
                    long tagBeginOffset = zone.Offset + cumulativeDelta;
                    long tagEndOffset = tagBeginOffset + zone.Size;

                    // Need to build a larger file
                    if (newTagSize > zone.Size)
                    {
                        StreamUtils.LengthenStream(w.BaseStream, tagEndOffset, (uint)(newTagSize - zone.Size));
                    }
                    else if (newTagSize < zone.Size) // Need to reduce file size
                    {
                        StreamUtils.ShortenStream(w.BaseStream, tagEndOffset, (uint)(zone.Size - newTagSize));
                    }

                    // Copy tag contents to the new slot
                    r.BaseStream.Seek(tagBeginOffset, SeekOrigin.Begin);
                    s.Seek(0, SeekOrigin.Begin);

                    if (newTagSize > zone.CoreSignature.Length)
                    {
                        StreamUtils.CopyStream(s, w.BaseStream, s.Length);
                    }
                    else
                    {
                        if (zone.CoreSignature.Length > 0) msw.Write(zone.CoreSignature);
                    }

                    cumulativeDelta += newTagSize - oldTagSize;
                }
            } // Loop through zones

            // TODO : Rewrite _first_ picture zone
            // NB : previously scattered picture blocks become contiguous after rewriting

            return result;
        }

        private bool writeVorbisTag(BinaryWriter w, TagData tag, bool isLast)
        {
            bool result;
            long sizePos, dataPos, finalPos;
            byte blockType = META_VORBIS_COMMENT;
            if (isLast) blockType = (byte)(blockType & 0x80);

            w.Write(blockType);
            sizePos = w.BaseStream.Position;
            w.Write(new byte[] { 0, 0, 0 }); // Placeholder for 24-bit integer that will be rewritten at the end of the method

            dataPos = w.BaseStream.Position;
            result = vorbisTag.Write(w.BaseStream, tag);

            finalPos = w.BaseStream.Position;
            w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
            w.Write(StreamUtils.EncodeBEUInt24((uint)(finalPos - dataPos)));
            w.BaseStream.Seek(finalPos, SeekOrigin.Begin);

            return result;
        }

        private bool writePictures(BinaryWriter w, IList<TagData.PictureInfo> pictures, bool isLast)
        {
            bool result = true;
            long sizePos, dataPos, finalPos;
            byte blockType;

            foreach (TagData.PictureInfo picture in pictures)
            {
                blockType = META_PICTURE;
                if (isLast) blockType = (byte)(blockType & 0x80);
                
                w.Write(blockType);
                sizePos = w.BaseStream.Position;
                w.Write(new byte[] { 0, 0, 0 }); // Placeholder for 24-bit integer that will be rewritten at the end of the method

                dataPos = w.BaseStream.Position;
                vorbisTag.WritePicture(w, picture.PictureData, picture.NativeFormat, Utils.GetMimeTypeFromImageFormat(picture.NativeFormat), picture.PicType.Equals(TagData.PIC_TYPE.Unsupported) ? picture.NativePicCode : ID3v2.EncodeID3v2PictureType(picture.PicType), "");

                finalPos = w.BaseStream.Position;
                w.BaseStream.Seek(sizePos, SeekOrigin.Begin);
                w.Write(StreamUtils.EncodeBEUInt24((uint)(finalPos - dataPos)));
                w.BaseStream.Seek(finalPos, SeekOrigin.Begin);
            }

            return result;
        }

        public bool Remove(BinaryWriter w)
        {
            TagData tag = vorbisTag.GetDeletionTagData();

            BinaryReader r = new BinaryReader(w.BaseStream);
            return Write(r, w, tag);
        }
    }
}