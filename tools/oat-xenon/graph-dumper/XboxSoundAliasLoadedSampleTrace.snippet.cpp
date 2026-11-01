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
                              << " sampleBytes=0 loadedOwned=0" << std::endl;
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
                        bool loadedOwned=false;
                        if(sf && sf->type==SAT_LOADED)
                        {
                            state="LOADED";
                            // A non-NULL source SoundFile pointer is NOT
                            // automatically a valid dereference. First match
                            // its address to a loaded non-reference asset in
                            // this exact original Xbox retail FF's XAsset
                            // pool. The loaded asset's OWN name determines
                            // the pinned OAT .xma output filename.
                            const auto* ptr=sf->u.loadSnd;
                            if(ptr)
                            {
                                for(const auto* owner:
                                    context.m_zone.m_pools.PoolAssets<AssetLoadedSound>())
                                {
                                    if(!owner || owner->IsReference() ||
                                       owner->Asset()!=ptr)continue;
                                    loadedOwned=true;
                                    loaded=safe(owner->m_name.c_str());
                                    bytes=ptr->sound.packet.BufferSize;
                                    break;
                                }
                            }
                        }
                        else if(sf && sf->type==SAT_STREAMED)
                        {
                            state="STREAMED";
                            dir=safe(sf->u.streamSnd.filename.dir);
                            name=safe(sf->u.streamSnd.filename.name);
                        }
                        // Genuine fully typed 184-byte Xbox snd_alias_t
                        // source metadata, read from the OAT-loaded owner,
                        // NOT a guessed/ordinal/donor native skeleton.
                        // Host pointers are intentionally scrubbed by the
                        // later 32-bit endian rebasing writer.
                        static_assert(sizeof(snd_alias_t)==184,
                                      "T4Xenon sound alias ABI drift");
                        static_assert(sizeof(SoundFile)==20,
                                      "T4Xenon SoundFile ABI drift");
                        const auto aliasHex=[&alias]()->std::string
                        {
                            static constexpr char digits[]="0123456789ABCDEF";
                            std::string out;
                            out.reserve(sizeof(alias)*2);
                            const auto* p=reinterpret_cast<const unsigned char*>(&alias);
                            for(size_t k=0;k<sizeof(alias);k++)
                            {
                                out.push_back(digits[p[k]>>4]);
                                out.push_back(digits[p[k]&15]);
                            }
                            return out;
                        };
                        std::cerr << "[WAW360-XMAALIASMETA]"
                                  << " root=" << asset.m_name
                                  << " index=" << i
                                  << " listName=" << safe(sound->aliasName)
                                  << " aliasName=" << safe(alias.aliasName)
                                  << " subtitle=" << safe(alias.subtitle)
                                  << " secondary=" << safe(alias.secondaryAliasName)
                                  << " chain=" << safe(alias.chainAliasName)
                                  << " type=" << (sf?static_cast<int>(sf->type):0)
                                  << " exists=" << (sf?static_cast<int>(sf->exists):0)
                                  << " loadedOwned=" << (loadedOwned?1:0)
                                  << " loadedName=" << loaded
                                  << " aliasHostBytes=" << aliasHex()
                                  << std::endl;
                        std::cerr << "[WAW360-XMASOUND] root=" << asset.m_name
                                  << " index=" << i
                                  << " type=" << state
                                  << " exists=" << (sf?static_cast<int>(sf->exists):0)
                                  << " alias=" << safe(alias.aliasName)
                                  << " loaded=" << loaded
                                  << " streamDir=" << dir
                                  << " streamName=" << name
                                  << " sampleBytes=" << bytes
                                  << " loadedOwned=" << (loadedOwned?1:0)
                                  << std::endl;
                    }
                }
            }
        }
