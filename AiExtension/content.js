chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.type === "FILL_FORM") {
    console.log("Gemini AI: Filling form with data", message.data);
    fillForm(message.data);
  }
});

function fillForm(data) {
  // Support both direct fields and nested fields/meisai
  if (data.fields && Array.isArray(data.fields)) {
    data.fields.forEach(field => {
      setValue(field.fieldId, field.value);
    });
  } else if (typeof data === 'object') {
    // Try to match object keys to element IDs or names
    Object.keys(data).forEach(key => {
      if (Array.isArray(data[key])) {
        // Handle list/grid data if possible
        fillGrid(key, data[key]);
      } else {
        setValue(key, data[key]);
      }
    });
  }
}

function setValue(idOrName, value) {
  // Try finding by ID
  let el = document.getElementById(idOrName);
  
  // If not found, try ASP.NET specific ID suffix matching
  if (!el) {
    const allInputs = document.querySelectorAll('input, select, textarea');
    for (let input of allInputs) {
      if (input.id.endsWith('_' + idOrName) || input.name.endsWith('$' + idOrName)) {
        el = input;
        break;
      }
    }
  }

  // Final try by name
  if (!el) {
    el = document.getElementsByName(idOrName)[0];
  }

  if (el) {
    el.value = value;
    // Trigger change events for WebForms scripts
    el.dispatchEvent(new Event('change', { bubbles: true }));
    el.dispatchEvent(new Event('input', { bubbles: true }));
    inputFlash(el);
  } else {
    console.warn(`Gemini AI: Could not find field ${idOrName}`);
  }
}

function fillGrid(gridName, rows) {
  console.log(`Gemini AI: Attempting to fill grid ${gridName} with ${rows.length} rows`);
  // This is highly dependent on the WebForms implementation (e.g. GridView)
  // Basic strategy: look for inputs that follow a pattern like ctl00_..._gridName_ctl02_txtCode
  rows.forEach((row, index) => {
    const rowNum = (index + 2).toString().padStart(2, '0'); // GridView usually starts ctl02
    Object.keys(row).forEach(key => {
      const searchPattern = `${gridName}_ctl${rowNum}_${key}`;
      // Search for any input ending with this pattern
      const inputs = document.querySelectorAll('input, select, textarea');
      for (let input of inputs) {
        if (input.id.includes(searchPattern)) {
          input.value = row[key];
          input.dispatchEvent(new Event('change', { bubbles: true }));
          inputFlash(input);
        }
      }
    });
  });
}

function inputFlash(el) {
  const originalBg = el.style.backgroundColor;
  el.style.backgroundColor = '#fff9c4'; // Light yellow
  setTimeout(() => {
    el.style.backgroundColor = originalBg;
  }, 1000);
}
