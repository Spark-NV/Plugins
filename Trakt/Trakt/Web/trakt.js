const TraktConfigurationPage = {
    pluginUniqueId: '4fe3201e-d6ae-4f2e-8917-e12bda571281',
    loadConfiguration: function (userId, page) {
        ApiClient.getPluginConfiguration(TraktConfigurationPage.pluginUniqueId).then(function (config) {
            let currentUserConfig = config.TraktUsers.filter(function (curr) {
                return curr.LinkedMbUserId == userId;
                //return true;
            })[0];
            // User doesn't have a config, so create a default one.
            if (!currentUserConfig) {
                currentUserConfig = {
                    AccessToken: null,
                    ExtraLogging: false
                };
            }
            
            const extraLoggingCheckbox = page.querySelector('#chkExtraLogging');
            if (extraLoggingCheckbox) {
                extraLoggingCheckbox.checked = currentUserConfig.ExtraLogging || false;
            }
            
            // List the folders the user can access for library paths
            ApiClient.getVirtualFolders(userId).then(function (result) {
                TraktConfigurationPage.loadLibraryPaths(result);
            });

            // Load Trakt lists if authorized
            if (currentUserConfig.AccessToken != null) {
                TraktConfigurationPage.loadTraktLists(userId, page, currentUserConfig.SelectedListId);
            }

            // Load library paths
            if (currentUserConfig.StubMovieLibraryPath) {
                const moviePathSelect = page.querySelector('#selectMovieLibraryPath');
                if (moviePathSelect) {
                    setTimeout(function() {
                        moviePathSelect.value = currentUserConfig.StubMovieLibraryPath;
                    }, 100);
                }
            }
            if (currentUserConfig.StubTvLibraryPath) {
                const tvPathSelect = page.querySelector('#selectTvLibraryPath');
                if (tvPathSelect) {
                    setTimeout(function() {
                        tvPathSelect.value = currentUserConfig.StubTvLibraryPath;
                    }, 100);
                }
            }

            setAuthorizationElements(page, currentUserConfig.AccessToken != null);
            Dashboard.hideLoadingMsg();
        });
    },
    populateUsers: function (users) {
        let html = '';
        for (let i = 0, length = users.length; i < length; i++) {
            const user = users[i];
            html += '<option value="' + user.Id + '">' + user.Name + '</option>';
        }
        document.querySelector('#selectUser').innerHTML = html;
    },
    loadLibraryPaths: function (virtualFolders) {
        const movieSelect = document.querySelector('#selectMovieLibraryPath');
        const tvSelect = document.querySelector('#selectTvLibraryPath');
        
        let html = '<option value="">Select a library path...</option>';
        for (let i = 0, length = virtualFolders.length; i < length; i++) {
            const virtualFolder = virtualFolders[i];
            for (let j = 0, locLength = virtualFolder.Locations.length; j < locLength; j++) {
                const location = virtualFolder.Locations[j];
                html += '<option value="' + location + '">' + location + '</option>';
            }
        }
        
        if (movieSelect) {
            movieSelect.innerHTML = html;
        }
        if (tvSelect) {
            tvSelect.innerHTML = html;
        }
    },
    loadTraktLists: function (userId, page, selectedListId) {
        const select = page.querySelector('#selectTraktList');
        if (!select) return;
        
        const headers = {
            accept: 'application/json'
        };
        const request = {
            url: ApiClient.getUrl('Trakt/Users/' + userId + '/Lists'),
            dataType: 'json',
            type: 'GET',
            headers: headers
        };
        
        ApiClient.fetch(request).then(function (lists) {
            console.log('Received lists from API:', lists);
            let html = '<option value="">Select a list...</option>';
            if (lists && lists.length > 0) {
                for (let i = 0, length = lists.length; i < length; i++) {
                    const list = lists[i];
                    console.log('Processing list:', list);
                    
                    // Try both camelCase and PascalCase property names for compatibility
                    const ids = list.ids || list.Ids;
                    const name = list.name || list.Name;
                    const itemCount = list.item_count || list.ItemCount || 0;
                    
                    console.log('List data - ids:', ids, 'name:', name, 'itemCount:', itemCount);
                    
                    // Prefer slug, fall back to Trakt ID
                    const slug = ids?.slug || ids?.Slug;
                    const traktId = ids?.trakt || ids?.Trakt;
                    const listId = slug || (traktId ? traktId.toString() : '');
                    const listName = name || 'Unnamed List';
                    
                    console.log('List ID:', listId, 'List Name:', listName);
                    
                    const selected = (selectedListId && (listId === selectedListId || traktId?.toString() === selectedListId)) ? ' selected' : '';
                    html += '<option value="' + listId + '"' + selected + '>' + listName + ' (' + itemCount + ' items)</option>';
                }
            }
            select.innerHTML = html;
        }).catch(function (error) {
            console.error('Error loading Trakt lists:', error);
            console.error('Error details:', error);
            select.innerHTML = '<option value="">Error loading lists</option>';
        });
    }
};

function setAuthorizationElements(page, isAuthorized) {
    let buttonText;
    if (isAuthorized) {
        page.querySelector('#activateWithCode').classList.add('hide');
        page.querySelector('#deauthorizeDevice').classList.remove('hide');
        page.querySelector('#authorizedDescription').classList.remove('hide');
        buttonText = 'Force re-authorization';
    } else {
        page.querySelector('#deauthorizeDevice').classList.add('hide');
        page.querySelector('#authorizedDescription').classList.add('hide');
        buttonText = 'Authorize device';
    }
    // Set the auth button
    page.querySelector('#authorizeDevice').textContent = buttonText;
    page.querySelector('#authorizeDevice').classList.remove('hide');
}

function save(page) {
    return new Promise((resolve) => {
        const currentUserId = page.querySelector('#selectUser').value;
        ApiClient.getPluginConfiguration(TraktConfigurationPage.pluginUniqueId).then(function (config) {
            let currentUserConfig = config.TraktUsers.filter(function (curr) {
                return curr.LinkedMbUserId == currentUserId;
            })[0];
            // User doesn't have a config, so create a default one.
            if (!currentUserConfig) {
                currentUserConfig = {};
                config.TraktUsers.push(currentUserConfig);
            }
            const extraLoggingCheckbox = page.querySelector('#chkExtraLogging');
            currentUserConfig.ExtraLogging = extraLoggingCheckbox ? extraLoggingCheckbox.checked : false;
            currentUserConfig.LinkedMbUserId = currentUserId;
            currentUserConfig.SelectedListId = page.querySelector('#selectTraktList').value || null;
            currentUserConfig.StubMovieLibraryPath = page.querySelector('#selectMovieLibraryPath').value || null;
            currentUserConfig.StubTvLibraryPath = page.querySelector('#selectTvLibraryPath').value || null;
            if (currentUserConfig.UserName == '') {
                config.TraktUsers.remove(config.TraktUsers.indexOf(currentUserConfig));
            }
            ApiClient.updatePluginConfiguration(TraktConfigurationPage.pluginUniqueId, config).then(function (result) {
                Dashboard.processPluginConfigurationUpdateResult(result);
                ApiClient.getUsers().then(function (users) {
                    const currentUserId = page.querySelector('#selectUser').value;
                    TraktConfigurationPage.populateUsers(users);
                    page.querySelector('#selectUser').value = currentUserId;
                    TraktConfigurationPage.loadConfiguration(currentUserId, page);
                    resolve();
                });
            });
        });
    });
}

export default function (view) {
    view.querySelector('#selectUser').addEventListener('change', function () {
        TraktConfigurationPage.loadConfiguration(this.value, view);
    });

    view.querySelector('#traktConfigurationForm').addEventListener('submit', function (e) {
        save(view);
        e.preventDefault();
        return false;
    });

    view.querySelector('#authorizeDevice').addEventListener('click', function (e) {
        const currentUserId = view.querySelector('#selectUser').value;
        const headers = {
            accept: 'application/json'
        };
        const request = {
            url: ApiClient.getUrl('Trakt/Users/' + currentUserId + '/Authorize'),
            dataType: 'json',
            type: 'POST',
            headers: headers
        };
        function handleError(result) {
            Dashboard.alert({
                message: 'An error occurred when trying to authorize device: ' + result.status + ' - ' + result.statusText
            });
        };
        ApiClient.fetch(request).then(function (result) {
            console.log('trakt.tv user code: ' + result.userCode);
            view.querySelector('#authorizedDescription').classList.add('hide');
            view.querySelector('#authorizeDevice').classList.add('hide');
            view.querySelector('#userCode').textContent = result.userCode;
            view.querySelector('#activateWithCode').classList.remove('hide');

            console.log('Polling for authorization.');
            request.url = ApiClient.getUrl('Trakt/Users/' + currentUserId + '/PollAuthorizationStatus');
            request.type = 'GET';
            ApiClient.fetch(request).then(function (result) {
                console.log('User is authorized: ' + result.isAuthorized);
                view.querySelector('#userCode').textContent = '';
                TraktConfigurationPage.loadConfiguration(currentUserId, view);
            }).catch(handleError);
        }).catch(handleError);
    });

    view.querySelector('#deauthorizeDevice').addEventListener('click', function (e) {
        const currentUserId = view.querySelector('#selectUser').value;
        const headers = {
            accept: 'application/json'
        };
        const request = {
            url: ApiClient.getUrl('Trakt/Users/' + currentUserId + '/Deauthorize'),
            dataType: 'json',
            type: 'POST',
            headers: headers
        };
        function handleError() {
            Dashboard.alert({
                message: 'An error occurred when trying to deauthorize device for user ' + currentUserId
            });
        };
        ApiClient.fetch(request).then(function () {
            view.querySelector('#authorizedDescription').classList.add('hide');
            view.querySelector('#authorizeDevice').classList.remove('hide');
            view.querySelector('#userCode').textContent = '';
            view.querySelector('#activateWithCode').classList.add('hide');
            TraktConfigurationPage.loadConfiguration(currentUserId, view);
        }).catch(handleError);
    });

    view.querySelector('#refreshLists').addEventListener('click', function (e) {
        const currentUserId = view.querySelector('#selectUser').value;
        const currentListId = view.querySelector('#selectTraktList').value;
        TraktConfigurationPage.loadTraktLists(currentUserId, view, currentListId);
    });

    view.querySelector('#createStubs').addEventListener('click', function (e) {
        const currentUserId = view.querySelector('#selectUser').value;
        const statusDiv = view.querySelector('#stubCreationStatus');
        const listSelect = view.querySelector('#selectTraktList');
        const moviePathSelect = view.querySelector('#selectMovieLibraryPath');
        const tvPathSelect = view.querySelector('#selectTvLibraryPath');
        
        if (!listSelect || !listSelect.value) {
            Dashboard.alert({
                message: 'Please select a Trakt list first.'
            });
            return;
        }
        
        if ((!moviePathSelect || !moviePathSelect.value) && (!tvPathSelect || !tvPathSelect.value)) {
            Dashboard.alert({
                message: 'Please select at least one library path (for movies or TV shows).'
            });
            return;
        }
        
        // Save configuration first to ensure list and paths are saved
        save(view).then(function() {
            statusDiv.classList.remove('hide');
            statusDiv.textContent = 'Creating stub files...';
            
            const headers = {
                accept: 'application/json'
            };
            const request = {
                url: ApiClient.getUrl('Trakt/Users/' + currentUserId + '/CreateStubs'),
                dataType: 'json',
                type: 'POST',
                headers: headers
            };
            
            ApiClient.fetch(request).then(function (result) {
                statusDiv.textContent = result.message || 'Stub files created successfully!';
                statusDiv.classList.remove('hide');
                setTimeout(function() {
                    statusDiv.classList.add('hide');
                }, 5000);
            }).catch(function (error) {
                statusDiv.textContent = 'Error creating stub files: ' + (error.message || error);
                statusDiv.classList.remove('hide');
                console.error('Error creating stubs:', error);
            });
        });
    });

    view.addEventListener('viewshow', function () {
        const page = this;
        ApiClient.getUsers().then(function (users) {
            TraktConfigurationPage.populateUsers(users);
            const currentUserId = page.querySelector('#selectUser').value;
            TraktConfigurationPage.loadConfiguration(currentUserId, page);
        });
    });
}
