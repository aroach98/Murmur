Add-Type -AssemblyName System.Speech
$s = New-Object System.Speech.Synthesis.SpeechSynthesizer
$fmt = New-Object System.Speech.AudioFormat.SpeechAudioFormatInfo(44100, [System.Speech.AudioFormat.AudioBitsPerSample]::Sixteen, [System.Speech.AudioFormat.AudioChannel]::Stereo)
$s.SetOutputToWaveFile($args[0], $fmt)
$s.Speak($args[1])
$s.Dispose()
Write-Output ("WAV written: " + (Get-Item $args[0]).Length + " bytes")
