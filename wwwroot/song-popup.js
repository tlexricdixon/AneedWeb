window.aneedSongPopup = {
  show: function (dialog) {
    if (dialog && !dialog.open) dialog.showModal();
  },
  close: function (dialog) {
    if (dialog && dialog.open) dialog.close();
  }
};
