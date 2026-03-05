function openTab(evt, tabName, contentTag, linkTag) {
  // Declare all variables
  var i, tabcontent, tablinks;

  // Get all elements with class="tabcontent" and hide them
  tabcontent = document.getElementsByClassName(contentTag);
  for (i = 0; i < tabcontent.length; i++) {
    tabcontent[i].style.display = "none";
  }

  // Get all elements with class="tablinks" and remove the class "active"
  tablinks = document.getElementsByClassName(linkTag);
  for (i = 0; i < tablinks.length; i++) {
    tablinks[i].className = tablinks[i].className.replace(" active", "");
  }

  // Show the current tab, and add an "active" class to the button that opened the tab
  document.getElementById(tabName).style.display = "block";
  evt.currentTarget.className += " active";
}

function openTabAPI(evt, tabName, contentTag, linkTag) {
  // Declare all variables
  var i, tabcontent, tablinks;

  // Get all elements with class="tabcontent" and hide them
  tabcontent = document.getElementsByClassName(contentTag);
  for (i = 0; i < tabcontent.length; i++) {
    if (tabcontent[i].className.indexOf(tabName) != -1)
    {
        tabcontent[i].style.display = "block";
    }
    else
    {
        tabcontent[i].style.display = "none";
    }
  }

  // Select all items as active
  tablinks = document.getElementsByClassName(linkTag);
  for (i = 0; i < tablinks.length; i++) {
    if (tablinks[i].className.indexOf(tabName) != -1)
    {
        tablinks[i].style.backgroundColor = "#ccc";
    }
    else
    {
        tablinks[i].style.backgroundColor = "inherit";
    }
  }
}
