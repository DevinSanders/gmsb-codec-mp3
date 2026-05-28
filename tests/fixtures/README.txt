Test fixtures
=============

sine-440hz-0.5s.mp3
  A 0.5-second, 440 Hz sine tone. Stereo, 44.1 kHz, MP3 CBR 64 kbps.
  Generated synthetically with ffmpeg:

    ffmpeg -f lavfi -i "sine=frequency=440:duration=0.5" \
           -ac 2 -ar 44100 -b:a 64k sine-440hz-0.5s.mp3

  Purely synthetic tone — contains no third-party copyrighted audio.
  Released into the public domain (CC0); free to redistribute with the
  test suite.
