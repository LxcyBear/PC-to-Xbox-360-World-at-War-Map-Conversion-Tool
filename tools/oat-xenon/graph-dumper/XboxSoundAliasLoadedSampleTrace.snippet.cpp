        // Read-only Xbox 360 OAT-loaded Sound -> SoundFile -> LoadedSound
        // chain. This is used only for exact PC-verified alias names, never
        // for guessing file identity from a coincidentally matching filename.
        const char* witness=std::getenv("WAW360_XBOX_FLAME_SOUND_CHAINS");
        if(witness && *witness && asset.m_name.size()<=240)
        {
            std::stringstream query(witness);
            std::string target;
            bool selected=false;
            while(std::getline(query,target,','))
                if(target==asset.m_name){selected=true;break;}
            if(selected)
            {
                // Log only bounded unambiguous path-like names. A malformed
                // source name remains explicitly blank, never guessed.
                const auto safe=[](const char* input)->std::string
                {
                    if(!input)return "-";
                    std::string value;
                    for(size_t i=0;i<240;++i)
                    {
                        const auto c=static_cast<unsigned char>(input[i]);
                        if(c==0)return value.empty()?"-":value;
                        if(!((c>='a'&&c<='z')||(c>='A'&&c<='Z')||
                             (c>='0'&&c<='9')||c=='_'||c=='-'||
                             c=='.'||c=='/'||c=='\\'))
                            return "-";
                        value.push_back(c=='\\'?'/':static_cast<char>(c));
                    }
                    return "-";
                };
                if(!sound || !sound->head || sound->count<=0 ||
                    sound->count>512)
                {
                    std::cerr << "[WAW360-XMASOUND] root=" << asset.m_name
                              << " index=-1 type=INVALID exists=0 alias=-"
                              << " loaded=- streamDir=- streamName=-"
                              << " sampleBytes=0" << std::endl;
                }
                else
                {
                    for(int i=0;i<sound->count;i++)
                    {
                        const auto& alias=sound->head[i];
                        const auto* sf=alias.soundFile;
                        std::string state=sf?"UNKNOWN":"NULL";
                        std::string loaded="-",dir="-",name="-";
                        uint32_t bytes=0;
                        if(sf && sf->type==SAT_LOADED)
                        {
                            state="LOADED";
                            const auto* ptr=sf->u.loadSnd;
                            if(ptr && ptr->name)
                            {
                                loaded=safe(ptr->name);
                                bytes=ptr->sound.packet.BufferSize;
                            }
                        }
                        else if(sf && sf->type==SAT_STREAMED)
                        {
                            state="STREAMED";
                            dir=safe(sf->u.streamSnd.filename.dir);
                            name=safe(sf->u.streamSnd.filename.name);
                        }
                        std::cerr << "[WAW360-XMASOUND] root=" << asset.m_name
                                  << " index=" << i
                                  << " type=" << state
                                  << " exists=" << (sf?static_cast<int>(sf->exists):0)
                                  << " alias=" << safe(alias.aliasName)
                                  << " loaded=" << loaded
                                  << " streamDir=" << dir
                                  << " streamName=" << name
                                  << " sampleBytes=" << bytes
                                  << std::endl;
                    }
                }
            }
        }
