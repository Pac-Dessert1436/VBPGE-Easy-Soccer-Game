Option Strict On
Option Infer On
Imports NAudio.Wave

Public Class SoundPlayer
    Private ReadOnly reader As AudioFileReader
    Private ReadOnly waveOut As WaveOutEvent
    Private isLooping As Boolean = False

    Public Sub New(filename As String)
        reader = New AudioFileReader(filename)
        waveOut = New WaveOutEvent
        waveOut.Init(reader)

        AddHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
    End Sub

    Public Sub Play()
        If waveOut IsNot Nothing Then
            isLooping = False
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub PlayLooping()
        If waveOut IsNot Nothing Then
            isLooping = True
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Public Sub [Stop]()
        If waveOut IsNot Nothing Then
            isLooping = False
            waveOut.Stop()
        End If
    End Sub

    Public Sub OnPlaybackStopped(sender As Object, e As StoppedEventArgs)
        If isLooping AndAlso waveOut IsNot Nothing Then
            reader.Position = 0
            waveOut.Play()
        End If
    End Sub

    Protected Overrides Sub Finalize()
        Try
            If waveOut IsNot Nothing Then
                RemoveHandler waveOut.PlaybackStopped, AddressOf OnPlaybackStopped
            End If
            waveOut?.Dispose()
            reader?.Dispose()
        Finally
            MyBase.Finalize()
        End Try
    End Sub
End Class
