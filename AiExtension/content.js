chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "FILL_FORM") {
    console.log("Gemini AI: Filling form with data", message.data);
    fillForm(message.data);
  }
});

function fillForm(data) {
  if (data.fields && Array.isArray(data.fields)) {
    data.fields.forEach(field => {
      setValue(field.fieldId, field.value);
    });
  } else if (typeof data === 'object') {
    Object.keys(data).forEach(key => {
      if (Array.isArray(data[key])) {
        fillGrid(key, data[key]);
      } else {
        setValue(key, data[key]);
      }
    });
  }
}

function setValue(idOrName, value) {
  let el = document.getElementById(idOrName);
  if (!el) {
    el = document.getElementsByName(idOrName)[0];
  }

  if (el) {
    el.value = value;
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('input', { bubbles: true }));
    inputFlash(el);
  } else {
    console.warn(`Gemini AI: Could not find field ${idOrName}`);
  }
}

function fillGrid(gridName, rows) {
  console.log(`Gemini AI: Attempting to fill grid ${gridName} with ${rows.length} rows`);
  const table = document.getElementById(gridName) || document.querySelector(`table[data-grid="${gridName}"]`);
  if (!table) {
    console.warn(`Gemini AI: Could not find table ${gridName}`);
    return;
  }

  const tbody = table.querySelector('tbody');
  if (!tbody) return;

  rows.forEach((row, index) => {
    const existingRow = tbody.rows[index];
    if (existingRow) {
      Object.keys(row).forEach(key => {
        const cell = existingRow.querySelector(`[data-field="${key}"]`);
        if (cell) {
          cell.textContent = row[key];
          inputFlash(cell);
        }
      });
    }
  });
}

function inputFlash(el) {
  const originalBg = el.style.backgroundColor;
  el.style.backgroundColor = '#fff9c4';
  setTimeout(() => {
    el.style.backgroundColor = originalBg;
  }, 1000);
}
