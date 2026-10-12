window.stationPlayer = {
 connect: reference => {
  const audio = document.getElementById('station-audio');
  const notify = failed => reference.invokeMethodAsync('PlaybackChanged', !audio.paused && !audio.error, failed).catch(() => {});
  audio.addEventListener('play', () => notify(false));
  audio.addEventListener('pause', () => notify(false));
  audio.addEventListener('ended', () => notify(false));
  audio.addEventListener('error', () => notify(true));
 },
 toggle: async () => { const audio = document.getElementById('station-audio'); if (!audio.paused) { audio.pause(); return false; } audio.volume = Number(document.querySelector('.volume input').value); await audio.play(); return true; },
 volume: value => { document.getElementById('station-audio').volume = value; }
};

document.addEventListener('error', event => {
 const target = event.target;
 if (target instanceof HTMLImageElement && !target.src.endsWith('/images/station.webp')) target.src = '/images/station.webp';
}, true);
